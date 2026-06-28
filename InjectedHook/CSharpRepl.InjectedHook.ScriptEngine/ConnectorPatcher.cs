// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using CSharpRepl.InjectedHook.Contracts;
using MonoMod.RuntimeDetour;

namespace CSharpRepl.InjectedHook.ScriptEngine;

/// <summary>
/// Owns live method-replacement state for the engine: resolves a named target method in the running app,
/// detours it to a REPL-produced delegate via MonoMod.RuntimeDetour, and tracks the resulting <see cref="Hook"/>
/// so it can be listed and undone. Pure reflection + detour mechanics; the engine supplies the evaluated
/// replacement delegate (it owns the script state). Not thread-safe on its own; the engine serializes calls
/// under its eval gate.
/// </summary>
internal sealed class ConnectorPatcher
{
    private sealed class Entry
    {
        public required int Id { get; init; }
        public required Hook Hook { get; init; }
        public required string Method { get; init; }
        public required string Replacement { get; init; }
        public required PatchMode Mode { get; init; }
    }

    private const BindingFlags MethodFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private readonly Dictionary<int, Entry> patches = new();
    private int nextId;

    /// <summary>
    /// Splits "Namespace.Type.Method" and finds candidate overloads on the resolved type from the target's
    /// loaded assemblies. Returns an empty list (and a reason) when the type or method can't be found.
    /// </summary>
    public IReadOnlyList<MethodInfo> ResolveCandidates(string targetMethod, out string? error)
    {
        error = null;
        var lastDot = targetMethod.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == targetMethod.Length - 1)
        {
            error = $"'{targetMethod}' is not a fully-qualified Type.Method reference.";
            return [];
        }

        var typeName = targetMethod[..lastDot];
        var methodName = targetMethod[(lastDot + 1)..];

        var type = FindType(typeName);
        if (type is null)
        {
            error = $"Could not find type '{typeName}' among the target's loaded assemblies.";
            return [];
        }

        // Skip open generic methods — each instantiation is a distinct native method, so there's nothing single to detour.
        var candidates = type
            .GetMethods(MethodFlags)
            .Where(m => m.Name == methodName && !m.IsGenericMethodDefinition && !m.IsAbstract)
            .ToList();

        if (candidates.Count == 0)
        {
            error = $"Type '{typeName}' has no patchable method named '{methodName}'.";
        }

        return candidates;
    }

    /// <summary>
    /// Picks the overload whose required replacement signature matches <paramref name="replacement"/>'s delegate
    /// shape (instance-first for instance methods; an extra leading "orig" delegate in Wrap mode).
    /// </summary>
    public MethodInfo? MatchOverload(IReadOnlyList<MethodInfo> candidates, Delegate replacement, PatchMode mode)
    {
        var invoke = replacement.GetType().GetMethod("Invoke")!;
        var actualParams = invoke.GetParameters().Select(p => p.ParameterType).ToArray();
        var actualReturn = invoke.ReturnType;

        return candidates.FirstOrDefault(m =>
        {
            try
            {
                var (expectedParams, expectedReturn) = ExpectedSignature(m, mode);
                return expectedReturn == actualReturn && expectedParams.SequenceEqual(actualParams);
            }
            catch
            {
                // ExpectedSignature throws for shapes Func/Action can't model (e.g. Wrap + by-ref);
                // such a candidate just isn't matchable on the delegate-value path.
                return false;
            }
        });
    }

    /// <summary>
    /// Builds the cast that coerces a method group (a named replacement) to the delegate a candidate expects.
    ///
    /// - Replace: emits a delegate TYPE declaration, so by-ref (ref/out/in) parameters are expressible — Func/Action
    ///   can't carry them. The engine prepends the declaration to the probe submission, then casts to its name.
    /// - Wrap (no by-ref): reuses a Func/Action cast, so the user's `orig` parameter (a shared Func type) still
    ///   matches what they wrote.
    /// - Returns null when the shape isn't expressible (Wrap + by-ref, or a ref-return), so the caller reports a
    ///   clean failure rather than emitting code that won't compile.
    /// </summary>
    public (string Declaration, string TypeName)? BuildCastDelegate(MethodInfo target, PatchMode mode, int salt)
    {
        if (target.ReturnType.IsByRef)
        {
            return null; // ref-returning methods aren't expressible as a Func/Action or a simple delegate parameter
        }

        if (mode == PatchMode.Wrap)
        {
            // Wrap needs a shared `orig` delegate type. Func works for by-value params; by-ref can't use Func and a
            // generated nominal type wouldn't match the type the user named, so by-ref wrap is unsupported here.
            if (target.GetParameters().Any(p => p.ParameterType.IsByRef))
            {
                return null;
            }
            var (paramTypes, returnType) = ExpectedSignature(target, mode);
            return ("", DelegateTypeText(paramTypes, returnType));
        }

        // Replace: emit a delegate type so ref/out/in are expressible.
        var name = $"__Repl_{salt}";
        var declaration = $"delegate {CompilableReturnName(target.ReturnType)} {name}({BuildParameterList(target)});";
        return (declaration, name);
    }

    /// <summary>The instance-first (for instance methods) parameter list for an emitted delegate, with ref/out/in modifiers.</summary>
    private static string BuildParameterList(MethodInfo target)
    {
        var parts = target.GetParameters().Select((p, i) => $"{ParameterText(p)} __p{i}");
        if (!target.IsStatic)
        {
            parts = parts.Prepend($"{CompilableTypeName(target.DeclaringType!)} __self");
        }
        return string.Join(", ", parts);
    }

    /// <summary>A by-ref-aware parameter type rendering: <c>out X</c> / <c>ref X</c> / <c>in X</c>, or just <c>X</c>.</summary>
    private static string ParameterText(ParameterInfo parameter)
    {
        // CompilableTypeName already strips IsByRef to the element type, so this works for both cases.
        var type = CompilableTypeName(parameter.ParameterType);
        if (!parameter.ParameterType.IsByRef)
        {
            return type;
        }
        var modifier = parameter.IsOut ? "out" : parameter.IsIn ? "in" : "ref";
        return $"{modifier} {type}";
    }

    /// <summary>A delegate return type must use the <c>void</c> keyword, not <c>System.Void</c>.</summary>
    private static string CompilableReturnName(Type returnType) =>
        returnType == typeof(void) ? "void" : CompilableTypeName(returnType);

    /// <summary>Applies the detour and records it, returning the patch id.</summary>
    public int Apply(MethodInfo target, Delegate replacement, string replacementText, PatchMode mode)
    {
        var hook = new Hook(target, replacement);
        var id = ++nextId;
        patches[id] = new Entry
        {
            Id = id,
            Hook = hook,
            Method = DisplaySignature(target),
            Replacement = replacementText,
            Mode = mode,
        };
        return id;
    }

    public PatchInfo[] List() => patches.Values
        .OrderBy(e => e.Id)
        .Select(e => new PatchInfo { Id = e.Id, Method = e.Method, Replacement = e.Replacement, Mode = e.Mode })
        .ToArray();

    public bool Revert(int id)
    {
        if (!patches.Remove(id, out var entry))
        {
            return false;
        }
        entry.Hook.Dispose();
        return true;
    }

    public int RevertAll()
    {
        var count = patches.Count;
        foreach (var entry in patches.Values)
        {
            try { entry.Hook.Dispose(); } catch { /* best effort */ }
        }
        patches.Clear();
        return count;
    }

    /// <summary>
    /// The delegate signature a replacement must have for <paramref name="method"/>: the method's own parameters,
    /// prefixed by the declaring type for an instance method, and (in Wrap mode) by an "orig" delegate of that
    /// same shape so the replacement can call the original.
    /// </summary>
    private static (Type[] Parameters, Type ReturnType) ExpectedSignature(MethodInfo method, PatchMode mode)
    {
        var methodParams = method.GetParameters().Select(p => p.ParameterType);
        Type[] baseParams = method.IsStatic ? [.. methodParams] : [method.DeclaringType!, .. methodParams];
        var returnType = method.ReturnType;

        if (mode == PatchMode.Replace)
        {
            return (baseParams, returnType);
        }

        // Wrap: the "orig" delegate has the original's (instance-first) signature.
        var origDelegateType = Expression.GetDelegateType([.. baseParams, returnType]);
        return ([origDelegateType, .. baseParams], returnType);
    }

    private static string DelegateTypeText(Type[] paramTypes, Type returnType)
    {
        if (returnType == typeof(void))
        {
            return paramTypes.Length == 0
                ? "global::System.Action"
                : $"global::System.Action<{string.Join(", ", paramTypes.Select(CompilableTypeName))}>";
        }

        var args = paramTypes.Append(returnType).Select(CompilableTypeName);
        return $"global::System.Func<{string.Join(", ", args)}>";
    }

    /// <summary>A fully-qualified, compilable C# name for a type (handles generics, arrays, nested types).</summary>
    private static string CompilableTypeName(Type type)
    {
        if (type.IsByRef)
        {
            // ref/out can't be expressed by Func/Action; surface as the element type so the cast fails clearly.
            return CompilableTypeName(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            return CompilableTypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var name = StripArity("global::" + (definition.FullName ?? definition.Name));
            var args = type.GetGenericArguments().Select(CompilableTypeName);
            return $"{name.Replace('+', '.')}<{string.Join(", ", args)}>";
        }

        return "global::" + (type.FullName ?? type.Name).Replace('+', '.');
    }

    /// <summary>A readable rendering of a method for the patch list / confirmations.</summary>
    private static string DisplaySignature(MethodInfo method)
    {
        var prefix = method.IsStatic ? "static " : "";
        var declaring = method.DeclaringType?.FullName ?? method.DeclaringType?.Name ?? "";
        var parameters = string.Join(", ", method.GetParameters().Select(p => $"{ShortName(p.ParameterType)} {p.Name}"));
        return $"{prefix}{ShortName(method.ReturnType)} {declaring}.{method.Name}({parameters})";
    }

    private static string ShortName(Type type)
    {
        if (type.IsGenericType)
        {
            var name = StripArity(type.Name);
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(ShortName))}>";
        }
        return type.Name;
    }

    /// <summary>Strips the CLR arity suffix (<c>`1</c>) from a generic type name.</summary>
    private static string StripArity(string name)
    {
        var tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (assembly.IsDynamic)
            {
                continue;
            }
            try
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type is not null)
                {
                    return type;
                }
            }
            catch
            {
                // Unreadable assembly metadata — skip and keep searching.
            }
        }
        return Type.GetType(fullName, throwOnError: false);
    }
}
