// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

//This file loaded from resources and injected into CSharpRepl session in the warm-up phase.

public static class __CSharpRepl_RuntimeHelper
{
    public static SpanOutput HandleSpanOutput<T>(System.Span<T> span) => SpanOutput.Create<T>(span, false);
    public static SpanOutput HandleSpanOutput<T>(System.ReadOnlySpan<T> span) => SpanOutput.Create(span, true);

    public static CharSpanOutput HandleSpanOutput(System.Span<char> span) => CharSpanOutput.Create(span, false);
    public static CharSpanOutput HandleSpanOutput(System.ReadOnlySpan<char> span) => CharSpanOutput.Create(span, true);

    public abstract class SpanOutputBase(int originalLength, bool spanWasReadOnly) 
        : System.Collections.IEnumerable
    {
        public readonly int OriginalLength = originalLength;
        public readonly bool SpanWasReadOnly = spanWasReadOnly;

        //Necessary for correct output formatting.
        public int Count => OriginalLength;

        public abstract System.Collections.IEnumerator GetEnumerator();
    }

    public sealed class SpanOutput(System.Array array, int originalLength, bool spanWasReadOnly) 
        : SpanOutputBase(originalLength, spanWasReadOnly)
    {
        private const int MaxLength = 1024;

        public readonly System.Array Array = array;

        public override System.Collections.IEnumerator GetEnumerator() => Array.GetEnumerator();

        public static SpanOutput Create<T>(System.ReadOnlySpan<T> span, bool spanWasReadOnly) => new(
            span[..System.Math.Min(MaxLength, span.Length)].ToArray(),
            span.Length,
            spanWasReadOnly);
    }

    public sealed class CharSpanOutput(string text, int originalLength, bool spanWasReadOnly) 
        : SpanOutputBase(originalLength, spanWasReadOnly)
    {
        private const int MaxLength = 10_000;

        public readonly string Text = text;

        public override System.Collections.IEnumerator GetEnumerator() => Text.GetEnumerator();

        public static CharSpanOutput Create(System.ReadOnlySpan<char> span, bool spanWasReadOnly)
        {
            var len = System.Math.Min(MaxLength, span.Length);
            System.Span<char> buffer = stackalloc char[len];
            span[..len].CopyTo(buffer);
            if (span.Length > len)
            {
                buffer[^1] = '.';
                buffer[^2] = '.';
                buffer[^3] = '.';
            }
            return new(buffer.ToString(), span.Length, spanWasReadOnly);
        }
    }
}