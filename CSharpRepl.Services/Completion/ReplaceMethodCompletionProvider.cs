// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services.Remote.Commands;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;

namespace CSharpRepl.Services.Completion;

/// <summary>
/// Completion for the connect-mode REPL commands <c>#replace</c> / <c>#wrap</c>. Roslyn doesn't understand them
/// (they're bad preprocessor directives in Script mode), so the built-in providers produce nothing useful on
/// such a line. This provider detects the command, rewrites the argument under the caret into a snippet Roslyn
/// CAN complete, delegates to the normal <see cref="CompletionService"/> on a throwaway document, and re-emits
/// the results as its own items.
///
/// - Target position (<c>#replace MyApp.Type.Meth</c>): wrapped in <c>nameof(...)</c> so namespaces, types, and
///   BOTH static and instance members complete (plain member access on a type name omits instance members).
/// - Replacement position (<c>#replace X.M with expr</c>): the expression is completed as ordinary script code,
///   against the submission chain (so the user's just-defined delegate/method is offered).
///
/// Registered only in the connect-mode workspace (see <c>WorkspaceManager</c>), so the local REPL is unaffected.
/// </summary>
[ExportCompletionProvider(nameof(ReplaceMethodCompletionProvider), LanguageNames.CSharp), Shared]
internal sealed class ReplaceMethodCompletionProvider : CompletionProvider
{
    // The argument-taking commands, each with the trailing space that precedes its argument.
    private static readonly string[] Commands =
        [ConnectorCommands.Replace.Token + " ", ConnectorCommands.Wrap.Token + " "];
    private const string WithSeparator = ConnectorCommands.WithSeparator;

    // Carried on each re-emitted item so GetDescriptionAsync can rebuild the synthetic document on demand.
    private const string SyntheticTextProperty = "CSharpRepl.SyntheticText";
    private const string SyntheticPositionProperty = "CSharpRepl.SyntheticPosition";

    [ImportingConstructor]
    public ReplaceMethodCompletionProvider() { }

    public override bool ShouldTriggerCompletion(SourceText text, int caretPosition, CompletionTrigger trigger, OptionSet options)
    {
        if (!TryRewrite(text, caretPosition, out _, out _))
        {
            return false;
        }

        return trigger.Kind switch
        {
            CompletionTriggerKind.Invoke or CompletionTriggerKind.InvokeAndCommitIfUnique => true,
            CompletionTriggerKind.Insertion => trigger.Character == '.' || IsIdentifierChar(trigger.Character),
            _ => false,
        };
    }

    public override async Task ProvideCompletionsAsync(CompletionContext context)
    {
        var text = await context.Document.GetTextAsync(context.CancellationToken).ConfigureAwait(false);
        if (!TryRewrite(text, context.Position, out var synthetic, out var syntheticPosition))
        {
            return;
        }

        var delegated = await GetDelegatedCompletionsAsync(context.Document, synthetic, syntheticPosition, context.CancellationToken).ConfigureAwait(false);
        if (delegated is null || delegated.ItemsList.Count == 0)
        {
            return;
        }

        // Only type/member/expression completions make sense on a #replace/#wrap line, so suppress the unrelated
        // built-in items (keywords, snippets) that would otherwise mix in from the bad-directive document.
        context.IsExclusive = true;

        foreach (var item in delegated.ItemsList)
        {
            context.AddItem(Reemit(item, synthetic, syntheticPosition));
        }
    }

    public override async Task<CompletionDescription?> GetDescriptionAsync(Document document, CompletionItem item, CancellationToken cancellationToken)
    {
        // Re-emitted items aren't owned by the built-in provider that produced them, so rebuild the synthetic
        // document, re-run completion, and forward to the real provider's description for the matching item.
        if (!item.Properties.TryGetValue(SyntheticTextProperty, out var synthetic) ||
            !item.Properties.TryGetValue(SyntheticPositionProperty, out var positionText) ||
            !int.TryParse(positionText, NumberStyles.None, CultureInfo.InvariantCulture, out var syntheticPosition))
        {
            return await base.GetDescriptionAsync(document, item, cancellationToken).ConfigureAwait(false);
        }

        var syntheticDoc = document.WithText(SourceText.From(synthetic));
        var service = CompletionService.GetService(syntheticDoc);
        if (service is null)
        {
            return await base.GetDescriptionAsync(document, item, cancellationToken).ConfigureAwait(false);
        }

        var delegated = await service.GetCompletionsAsync(syntheticDoc, syntheticPosition, cancellationToken: cancellationToken).ConfigureAwait(false);
        var match = delegated?.ItemsList.FirstOrDefault(i => i.DisplayText == item.DisplayText && i.SortText == item.SortText);
        return match is null
            ? await base.GetDescriptionAsync(document, item, cancellationToken).ConfigureAwait(false)
            : await service.GetDescriptionAsync(syntheticDoc, match, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CompletionList?> GetDelegatedCompletionsAsync(Document document, string synthetic, int syntheticPosition, CancellationToken cancellationToken)
    {
        var syntheticDoc = document.WithText(SourceText.From(synthetic));
        var service = CompletionService.GetService(syntheticDoc);
        if (service is null)
        {
            return null;
        }
        return await service.GetCompletionsAsync(syntheticDoc, syntheticPosition, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static CompletionItem Reemit(CompletionItem item, string synthetic, int syntheticPosition)
    {
        var properties = item.Properties
            .SetItem(SyntheticTextProperty, synthetic)
            .SetItem(SyntheticPositionProperty, syntheticPosition.ToString(CultureInfo.InvariantCulture));

        return CompletionItem.Create(
            displayText: item.DisplayText,
            filterText: item.FilterText,
            sortText: item.SortText,
            properties: properties,
            tags: item.Tags,
            rules: item.Rules,
            displayTextPrefix: item.DisplayTextPrefix,
            displayTextSuffix: item.DisplayTextSuffix,
            inlineDescription: item.InlineDescription);
    }

    /// <summary>
    /// If the caret sits in the argument of a <c>#replace</c>/<c>#wrap</c> command, produces the synthetic
    /// snippet (and caret within it) to complete against. Returns false for any other line.
    /// </summary>
    private static bool TryRewrite(SourceText text, int caretPosition, out string synthetic, out int syntheticPosition)
    {
        synthetic = "";
        syntheticPosition = 0;

        var line = text.Lines.GetLineFromPosition(caretPosition);
        var lineToCaret = text.ToString(TextSpan.FromBounds(line.Start, caretPosition));
        var leadingWhitespace = lineToCaret.Length - lineToCaret.AsSpan().TrimStart().Length;
        var afterWhitespace = lineToCaret[leadingWhitespace..];

        var command = Commands.FirstOrDefault(c => afterWhitespace.StartsWith(c, StringComparison.OrdinalIgnoreCase));
        if (command is null)
        {
            return false;
        }

        var argument = afterWhitespace[command.Length..];
        var withIndex = argument.IndexOf(WithSeparator, StringComparison.OrdinalIgnoreCase);
        synthetic = withIndex >= 0
            ? argument[(withIndex + WithSeparator.Length)..]   // expression: complete as ordinary script code
            : "_ = nameof(" + argument;                        // target: nameof so static+instance members show
        syntheticPosition = synthetic.Length;
        return true;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
