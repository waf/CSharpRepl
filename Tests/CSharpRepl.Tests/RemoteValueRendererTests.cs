// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using CSharpRepl.InjectedHook.Contracts;
using CSharpRepl.Services.Remote;
using CSharpRepl.Services.Roslyn.Formatting;
using CSharpRepl.Services.SyntaxHighlighting;
using CSharpRepl.Services.Theming;
using Microsoft.Extensions.Caching.Memory;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using Xunit;

namespace CSharpRepl.Tests;

/// <summary>
/// Unit tests for the controller-side <see cref="RemoteValueRenderer"/> — it turns a theme-agnostic
/// <see cref="RemoteValue"/> (produced by the inspector in the target process) into themed console output,
/// mirroring the local REPL's simple-vs-detailed behavior. These need no running target: they feed
/// hand-built projections straight through the renderer.
/// </summary>
public class RemoteValueRendererTests
{
    private readonly RemoteValueRenderer renderer = new(
        new SyntaxHighlighter(new MemoryCache(new MemoryCacheOptions()), new Theme(null, null, null, null, [])));

    [Fact]
    public void Scalar_RendersDisplayText()
    {
        var value = new RemoteValue { Kind = RemoteValueKind.Scalar, TypeName = "int", DisplayText = "42", Style = RemoteValueStyle.Number };
        Assert.Equal("42", Render(value, Level.FirstSimple));
    }

    [Fact]
    public void Null_RendersNullLiteral()
    {
        Assert.Equal("null", Render(RemoteValue.Null, Level.FirstSimple));
    }

    [Fact]
    public void Collection_Simple_RendersInlineSummary()
    {
        var value = Collection("int[]", 3, "10", "20", "30");
        var output = Render(value, Level.FirstSimple);
        Assert.Contains("int[]", output);
        Assert.Contains("(3)", output);
        Assert.Contains("10", output);
        Assert.Contains("20", output);
        Assert.Contains("30", output);
    }

    [Fact]
    public void Collection_Detailed_RendersEachItem()
    {
        var value = Collection("int[]", 3, "10", "20", "30");
        var output = Render(value, Level.FirstDetailed);
        Assert.Contains("10", output);
        Assert.Contains("20", output);
        Assert.Contains("30", output);
    }

    [Fact]
    public void Object_Simple_RendersSummaryOnly()
    {
        var value = ServiceObject();
        var output = Render(value, Level.FirstSimple);
        Assert.Contains("Service", output);
        Assert.DoesNotContain("Value", output); // members only expand at the detailed level
    }

    [Fact]
    public void Object_Detailed_RendersMembers()
    {
        var value = ServiceObject();
        var output = Render(value, Level.FirstDetailed);
        Assert.Contains("Service", output);
        Assert.Contains("Value", output);
        Assert.Contains("42", output);
    }

    [Fact]
    public void RenderToPlainText_Scalar_IsTheDisplayTextWithNoAnsi()
    {
        var value = new RemoteValue { Kind = RemoteValueKind.Scalar, TypeName = "int", DisplayText = "42", Style = RemoteValueStyle.Number };
        var output = renderer.RenderToPlainText(value, Level.FirstSimple);
        Assert.Equal("42", output);
        Assert.DoesNotContain('\x1b', output); // no ANSI escape sequences — safe to capture/pipe
    }

    [Fact]
    public void RenderToPlainText_Collection_RendersInlineSummary()
    {
        var value = Collection("int[]", 3, "10", "20", "30");
        var output = renderer.RenderToPlainText(value, Level.FirstSimple);
        Assert.Equal("int[](3) { 10, 20, 30 }", output);
    }

    [Fact]
    public void Exception_PlainTextIsTheMessage()
    {
        var exception = new RemoteException { TypeName = "System.InvalidOperationException", Message = "boom", Detail = "System.InvalidOperationException: boom\n   at ..." };
        var (_, plainText) = renderer.RenderException(exception, Level.FirstSimple);
        Assert.Equal("boom", plainText);
    }

    private static RemoteValue Collection(string typeName, int count, params string[] items)
    {
        var projected = new RemoteValue[items.Length];
        for (var i = 0; i < items.Length; i++)
            projected[i] = new RemoteValue { Kind = RemoteValueKind.Scalar, TypeName = "int", DisplayText = items[i], Style = RemoteValueStyle.Number };
        return new RemoteValue { Kind = RemoteValueKind.Collection, TypeName = typeName, Count = count, Items = projected };
    }

    private static RemoteValue ServiceObject() => new()
    {
        Kind = RemoteValueKind.Object,
        TypeName = "Service",
        DisplayText = "Service",
        Style = RemoteValueStyle.TypeName,
        Members =
        [
            new RemoteMember { Name = "Value", Value = new RemoteValue { Kind = RemoteValueKind.Scalar, TypeName = "int", DisplayText = "42", Style = RemoteValueStyle.Number } },
        ],
    };

    private string Render(RemoteValue value, Level level) => ToString(renderer.Render(value, level));

    private static string ToString(IRenderable renderable)
    {
        const int Width = 1000;
        var options = new RenderOptions(new TestCapabilities(), new Size(Width, 1000));
        var builder = new StringBuilder();
        foreach (var segment in renderable.Render(options, Width))
        {
            builder.Append(segment.Text);
        }
        return builder.ToString();
    }
}
