using CSharpRepl.Services.Theming;
using Spectre.Console;

namespace CSharpRepl.Services.Extensions;

internal static class SpectreExtensions
{
    public static Paragraph Append(this Paragraph paragraph, StyledStringSegment text)
        => paragraph.Append(text.Text, text.Style);
}