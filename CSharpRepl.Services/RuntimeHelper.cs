// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

//This file loaded from resources and injected into CSharpRepl session in the warm-up phase.

public static class __CSharpRepl_RuntimeHelper
{
    public static SpanOutput HandleSpanOutput<T>(System.Span<T> span) => SpanOutput.Create<T>(span, typeof(System.Span<T>));
    public static SpanOutput HandleSpanOutput<T>(System.ReadOnlySpan<T> span) => SpanOutput.Create(span, typeof(System.ReadOnlySpan<T>));

    public static CharSpanOutput HandleSpanOutput(System.Span<char> span) => CharSpanOutput.Create(span, typeof(System.Span<char>));
    public static CharSpanOutput HandleSpanOutput(System.ReadOnlySpan<char> span) => CharSpanOutput.Create(span, typeof(System.ReadOnlySpan<char>));

    public static SpanOutput HandleMemoryOutput<T>(System.Memory<T> memory) => SpanOutput.Create<T>(memory.Span, typeof(System.Memory<T>));
    public static SpanOutput HandleMemoryOutput<T>(System.ReadOnlyMemory<T> memory) => SpanOutput.Create(memory.Span, typeof(System.ReadOnlyMemory<T>));

    public static CharSpanOutput HandleMemoryOutput(System.Memory<char> memory) => CharSpanOutput.Create(memory.Span, typeof(System.Memory<char>));
    public static CharSpanOutput HandleMemoryOutput(System.ReadOnlyMemory<char> memory) => CharSpanOutput.Create(memory.Span, typeof(System.ReadOnlyMemory<char>));

    public static RefStructOutput HandleRefStructOutput(string text) => new(text);

    public abstract class SpanOutputBase(int originalLength, System.Type originalType)
        : System.Collections.IEnumerable
    {
        public readonly int OriginalLength = originalLength;
        public readonly System.Type OriginalType = originalType;

        //Necessary for correct output formatting.
        public int Count => OriginalLength;

        public abstract System.Collections.IEnumerator GetEnumerator();
    }

    public sealed class SpanOutput(System.Array array, int originalLength, System.Type originalType)
        : SpanOutputBase(originalLength, originalType)
    {
        private const int MaxLength = 1024;

        private readonly System.Array array = array;

        public override System.Collections.IEnumerator GetEnumerator() => array.GetEnumerator();

        public static SpanOutput Create<T>(System.ReadOnlySpan<T> span, System.Type originalType) => new(
            span[..System.Math.Min(MaxLength, span.Length)].ToArray(),
            span.Length,
            originalType);
    }

    public sealed class CharSpanOutput(string text, int originalLength, System.Type originalType)
        : SpanOutputBase(originalLength, originalType)
    {
        private const int MaxLength = 10_000;

        public readonly string Text = text;

        public override System.Collections.IEnumerator GetEnumerator() => Text.GetEnumerator();

        public static CharSpanOutput Create(System.ReadOnlySpan<char> span, System.Type originalType)
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
            return new(buffer.ToString(), span.Length, originalType);
        }
    }

    public class RefStructOutput(string text)
    {
        private readonly string text = text;
        public override string ToString() => text;
    }
}