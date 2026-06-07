// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Inspector.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace CSharpRepl.Inspector.Engine;

/// <summary>
/// A trimmed relocation of the local REPL's <c>ScriptRunner</c> core (CSharpRepl.Services ScriptRunner.cs),
/// hosted inside the isolated EngineALC. Holds the persisted <c>ScriptState</c> chain, builds compilation
/// references from the <em>target's</em> loaded assemblies, and registers those live <see cref="System.Reflection.Assembly"/>
/// instances with Roslyn's loader so submissions compile against the target's types and bind at runtime to
/// the already-loaded default-ALC instances (full local-REPL parity via <c>ContinueWithAsync</c>).
/// </summary>
public sealed class InspectorEngine : IInspectorEngine
{
    private static readonly string[] DefaultImports =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Threading.Tasks",
    ];

    // Projection limits — keep the wire payload bounded and the projection fast/acyclic. Depth 1 mirrors the
    // local REPL's detailed view (top object's members, each shown as a scalar or a type-name summary), so a
    // member that points back at its parent is rendered as a summary rather than recursed into.
    private const int MaxDepth = 1;
    private const int MaxMembers = 100;
    private const int MaxItems = 100;

    // Cap a single scalar's text so a multi-megabyte string member can't ship whole over the wire (the 64 MB
    // frame bound is the last-resort backstop; this keeps the common case small). Truncated text is marked.
    private const int MaxScalarTextLength = 10_000;

    private readonly SemaphoreSlim gate = new(1, 1);
    private InteractiveAssemblyLoader assemblyLoader = null!;
    private ScriptOptions scriptOptions = null!;
    private string[] referencePaths = [];
    private ScriptState<object>? state;
    private bool initialized;

    public async Task<EvalResponse> EvalAsync(string code, bool detailed, CancellationToken cancellationToken)
    {
        // Submissions share the persisted chain, so they must run one at a time.
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();

            try
            {
                if (state is null)
                {
                    state = await CSharpScript
                        .Create(code, scriptOptions, globalsType: typeof(InspectorGlobals), assemblyLoader: assemblyLoader)
                        .RunAsync(globals: new InspectorGlobals(), cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    state = await state.ContinueWithAsync(code, scriptOptions, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (CompilationErrorException compileError)
            {
                // Compile failures don't extend the chain — report them but leave prior state intact.
                return EvalResponse.FromException(ProjectCompilationError(compileError), committed: false);
            }

            // ContinueWithAsync throws runtime exceptions (it doesn't capture them into ScriptState.Exception),
            // so this branch is effectively dead for our overload — kept defensively in case a future overload
            // captures-and-continues. A captured exception means the submission still committed to the chain.
            if (state.Exception is not null)
                return EvalResponse.FromException(ProjectException(state.Exception), committed: true);

            // Value-vs-void detection (the M3 port of ScriptRunner.HasValueReturningStatement): a value-returning
            // submission renders its result (which may itself be null → "null"); anything else renders as void.
            var isValueReturning = await IsValueReturningAsync(state, cancellationToken).ConfigureAwait(false);
            return isValueReturning
                ? EvalResponse.FromValue(ProjectValue(state.ReturnValue, depth: 0, detailed), committed: true)
                : EvalResponse.Void(committed: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception unexpected)
        {
            // A thrown runtime exception lands here. Per the verified ContinueWithAsync semantics it did NOT
            // advance the state chain (state still points at the prior submission), so committed: false.
            return EvalResponse.FromException(ProjectException(unexpected), committed: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetReferencePathsAsync(CancellationToken cancellationToken)
    {
        // EnsureInitialized resolves exactly the paths the controller's remote editor workspace needs, so build
        // the reference set (if it hasn't been built yet) under the same gate the eval path uses.
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            return referencePaths;
        }
        finally
        {
            gate.Release();
        }
    }

    private void EnsureInitialized()
    {
        if (initialized) return;

        assemblyLoader = new InteractiveAssemblyLoader();
        var references = new List<MetadataReference>();
        var paths = new List<string>();

        // Snapshot the target's live, loaded assemblies once (the spike showed re-enumerating the default
        // ALC each round picks up accumulating submission assemblies). Late-loaded target assemblies/#r are
        // best-effort refreshed later (M5). By the time a controller connects, the target's Main is running,
        // so its own assemblies are loaded and reachable here.
        foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (assembly.IsDynamic) continue;

            var location = assembly.Location;
            if (string.IsNullOrEmpty(location)) continue; // single-file/in-memory — no on-disk image (see SingleFileTarget spike)

            try
            {
                references.Add(MetadataReference.CreateFromFile(location));
            }
            catch
            {
                continue; // unreadable image — skip rather than fail the whole engine
            }

            paths.Add(location);

            // Pin identity to the already-loaded instance. A safety net rather than the binding mechanism:
            // submissions also reach live default-ALC objects via the submission ALC's fallback to Default.
            try { assemblyLoader.RegisterDependency(assembly); } catch { /* non-fatal */ }
        }

        scriptOptions = ScriptOptions.Default
            .WithReferences(references)
            .WithImports(DefaultImports)
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithAllowUnsafe(true);

        referencePaths = [.. paths];
        initialized = true;
    }

    /// <summary>
    /// Port of <c>ScriptRunner.HasValueReturningStatement</c>: the just-run submission produces a displayable
    /// value iff its last top-level statement is an expression statement with a <em>missing</em> semicolon and
    /// a non-void converted type. We read the syntax and semantic model straight off the compilation Roslyn
    /// produced for this submission, so this is authoritative (no re-parse).
    /// </summary>
    private static async Task<bool> IsValueReturningAsync(ScriptState<object> state, CancellationToken cancellationToken)
    {
        try
        {
            if (state.Script.GetCompilation() is not CSharpCompilation compilation)
                return state.ReturnValue is not null; // fall back to the M1 heuristic

            var tree = compilation.SyntaxTrees.LastOrDefault();
            if (tree is null)
                return false;

            var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is CompilationUnitSyntax { Members.Count: > 0 } unit &&
                unit.Members[^1] is GlobalStatementSyntax { Statement: ExpressionStatementSyntax { SemicolonToken.IsMissing: true } statement })
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                var convertedType = semanticModel.GetTypeInfo(statement.Expression, cancellationToken).ConvertedType;
                return convertedType?.SpecialType is not (null or SpecialType.System_Void);
            }

            return false;
        }
        catch
        {
            // Any analysis failure: degrade to the M1 heuristic rather than misreporting the whole eval.
            return state.ReturnValue is not null;
        }
    }

    private RemoteValue ProjectValue(object? value, int depth, bool detailed)
    {
        if (value is null)
            return RemoteValue.Null;

        var type = value.GetType();

        // Enums aren't "primitive" to ObjectDisplay but the local PrimitiveFormatter renders them as their name.
        if (type.IsEnum)
            return Scalar(type, value.ToString() ?? "", RemoteValueStyle.None);

        if (TryFormatScalar(value, out var text, out var style))
            return Scalar(type, text, style);

        // string is handled by TryFormatScalar; any other IEnumerable is a collection.
        if (value is IEnumerable enumerable)
            return ProjectCollection(enumerable, type, depth, detailed);

        return ProjectObject(value, type, depth, detailed);
    }

    private static RemoteValue Scalar(Type type, string text, RemoteValueStyle style) => new()
    {
        Kind = RemoteValueKind.Scalar,
        TypeName = FriendlyTypeName(type),
        DisplayText = text,
        Style = style,
    };

    /// <summary>
    /// Formats primitives/strings/chars to match the local REPL's PrimitiveFormatter options (quoted/escaped
    /// strings and chars, invariant-culture numbers, decimal radix, no type suffix). Returns false for anything
    /// that isn't a scalar (objects, collections, DateTime, etc.), which is then projected structurally.
    /// </summary>
    private static bool TryFormatScalar(object value, out string text, out RemoteValueStyle style)
    {
        switch (value)
        {
            case bool b:
                text = b ? "true" : "false";
                style = RemoteValueStyle.Keyword;
                return true;
            case char c:
                text = "'" + Escape(c.ToString(), quote: '\'') + "'";
                style = RemoteValueStyle.String;
                return true;
            case string s:
                // Escape only up to the cap so the wire payload stays bounded; mark when text was dropped.
                var truncated = s.Length > MaxScalarTextLength;
                var shown = truncated ? s[..MaxScalarTextLength] : s;
                text = "\"" + Escape(shown, quote: '"') + (truncated ? "…\" (+" + (s.Length - MaxScalarTextLength) + " more chars)" : "\"");
                style = RemoteValueStyle.String;
                return true;
            case sbyte or byte or short or ushort or int or uint or long or ulong
                or float or double or decimal:
                text = ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
                style = RemoteValueStyle.Number;
                return true;
            default:
                text = "";
                style = RemoteValueStyle.None;
                return false;
        }
    }

    private static string Escape(string value, char quote)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append(@"\\"); break;
                case '\0': builder.Append(@"\0"); break;
                case '\a': builder.Append(@"\a"); break;
                case '\b': builder.Append(@"\b"); break;
                case '\f': builder.Append(@"\f"); break;
                case '\n': builder.Append(@"\n"); break;
                case '\r': builder.Append(@"\r"); break;
                case '\t': builder.Append(@"\t"); break;
                case '\v': builder.Append(@"\v"); break;
                case var _ when c == quote: builder.Append('\\').Append(quote); break;
                case var _ when char.IsControl(c): builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: builder.Append(c); break;
            }
        }
        return builder.ToString();
    }

    private RemoteValue ProjectObject(object value, Type type, int depth, bool detailed)
    {
        var (summary, isTypeNameFallback) = ObjectSummary(value, type);

        // Project members only for the detailed view, and only at the top level — mirrors the local REPL
        // (which reflects over members for the detailed tree only) and avoids invoking property getters for
        // the one-line simple summary.
        IReadOnlyList<RemoteMember>? members = null;
        var truncated = false;
        if (detailed && depth < MaxDepth)
            (members, truncated) = ProjectMembers(value, type, depth, detailed);

        return new RemoteValue
        {
            Kind = RemoteValueKind.Object,
            TypeName = FriendlyTypeName(type),
            DisplayText = summary,
            Style = isTypeNameFallback ? RemoteValueStyle.TypeName : RemoteValueStyle.None,
            Members = members,
            Truncated = truncated,
        };
    }

    private static (string Summary, bool IsTypeNameFallback) ObjectSummary(object value, Type type)
    {
        if (HasOverriddenToString(type))
        {
            try
            {
                var text = value.ToString();
                if (!string.IsNullOrEmpty(text))
                    return (text, false);
            }
            catch { /* fall through to the type name */ }
        }
        return (FriendlyTypeName(type), true);
    }

    private (IReadOnlyList<RemoteMember> Members, bool Truncated) ProjectMembers(object value, Type type, int depth, bool detailed)
    {
        var members = new List<RemoteMember>();
        var truncated = false;

        foreach (var member in EnumerateReadableMembers(type))
        {
            if (members.Count >= MaxMembers)
            {
                truncated = true;
                break;
            }

            members.Add(new RemoteMember
            {
                Name = member.Name,
                Value = ProjectMemberValue(value, member, depth, detailed),
            });
        }

        return (members, truncated);
    }

    private RemoteValue ProjectMemberValue(object owner, MemberInfo member, int depth, bool detailed)
    {
        try
        {
            var memberValue = member switch
            {
                PropertyInfo property => property.GetValue(owner),
                FieldInfo field => field.GetValue(owner),
                _ => null,
            };
            return ProjectValue(memberValue, depth + 1, detailed);
        }
        catch (Exception exception)
        {
            // A throwing getter shouldn't sink the whole projection — surface it like the local REPL's !<...>.
            var inner = (exception as TargetInvocationException)?.InnerException ?? exception;
            return new RemoteValue
            {
                Kind = RemoteValueKind.Scalar,
                DisplayText = $"!<{inner.GetType().Name}>",
                Style = RemoteValueStyle.None,
            };
        }
    }

    private static IEnumerable<MemberInfo> EnumerateReadableMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

        var properties = type
            .GetProperties(flags)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

        var fields = type.GetFields(flags);

        return properties
            .Cast<MemberInfo>()
            .Concat(fields)
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Name, StringComparer.Ordinal);
    }

    private RemoteValue ProjectCollection(IEnumerable enumerable, Type type, int depth, bool detailed)
    {
        var items = new List<RemoteValue>();
        var truncated = false;
        int? count = (enumerable as ICollection)?.Count;

        try
        {
            foreach (var item in enumerable)
            {
                if (items.Count >= MaxItems)
                {
                    truncated = true;
                    break;
                }
                items.Add(ProjectValue(item, depth + 1, detailed));
            }
        }
        catch
        {
            // A lazy/throwing enumerator stops the projection with whatever we already gathered.
            truncated = true;
        }

        return new RemoteValue
        {
            Kind = RemoteValueKind.Collection,
            TypeName = FriendlyTypeName(type),
            Items = items,
            Count = count ?? (truncated ? null : items.Count),
            Truncated = truncated,
        };
    }

    private static bool HasOverriddenToString(Type type)
    {
        var method = type.GetMethod(nameof(ToString), BindingFlags.Public | BindingFlags.Instance, binder: null, types: Type.EmptyTypes, modifiers: null);
        return method is not null && method.DeclaringType != typeof(object);
    }

    /// <summary>A compact C#-ish type name (keywords for primitives, <c>T?</c>, <c>T[]</c>, <c>List&lt;int&gt;</c>).</summary>
    private static string FriendlyTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return FriendlyTypeName(underlying) + "?";

        if (type.IsArray)
            return FriendlyTypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";

        if (TryGetKeyword(type, out var keyword))
            return keyword;

        if (type.IsGenericType)
        {
            var name = type.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0) name = name[..tick];
            var args = type.GetGenericArguments().Select(FriendlyTypeName);
            return $"{name}<{string.Join(", ", args)}>";
        }

        return type.Name;
    }

    private static bool TryGetKeyword(Type type, out string keyword)
    {
        keyword = Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "bool",
            TypeCode.Byte => "byte",
            TypeCode.SByte => "sbyte",
            TypeCode.Int16 => "short",
            TypeCode.UInt16 => "ushort",
            TypeCode.Int32 => "int",
            TypeCode.UInt32 => "uint",
            TypeCode.Int64 => "long",
            TypeCode.UInt64 => "ulong",
            TypeCode.Single => "float",
            TypeCode.Double => "double",
            TypeCode.Decimal => "decimal",
            TypeCode.Char => "char",
            TypeCode.String => "string",
            _ => "",
        };
        if (keyword.Length > 0) return true;
        if (type == typeof(object)) { keyword = "object"; return true; }
        return false;
    }

    private static RemoteException ProjectException(Exception exception) => new()
    {
        TypeName = exception.GetType().FullName ?? exception.GetType().Name,
        Message = exception.Message,
        Detail = exception.ToString(),
    };

    private static RemoteException ProjectCompilationError(CompilationErrorException compileError)
    {
        var diagnostics = string.Join(Environment.NewLine, compileError.Diagnostics.Select(d => d.ToString()));
        return new RemoteException
        {
            TypeName = "CompilationError",
            Message = string.IsNullOrEmpty(diagnostics) ? compileError.Message : diagnostics,
            Detail = compileError.ToString(),
        };
    }
}
