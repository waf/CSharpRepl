// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

//Modified copy of https://github.com/dotnet/roslyn/blob/main/src/Workspaces/Core/Portable/Shared/Utilities/XmlFragmentParser.cs

#nullable disable

using System;
using System.Diagnostics;
using System.IO;
using System.Xml;

namespace CSharpRepl.Services.Roslyn;

/// <summary>
/// An XML parser that is designed to parse small fragments of XML such as those that appear in documentation comments.
/// </summary>
/// <remarks>
/// Each <see cref="ParseFragment{TArg}"/> call uses its own freshly-created <see cref="XmlReader"/> over its own
/// <see cref="Reader"/> and keeps no state between calls. An earlier version re-used a single <see cref="XmlReader"/>
/// over a single, rewindable <see cref="Reader"/> across successive parses to save allocations. That shared, stateful
/// reader could fall out of sync with <see cref="XmlReader"/>'s internal character buffer and corrupt memory,
/// surfacing as an intermittent <see cref="AccessViolationException"/> that crashed the process (observed only on
/// Linux/.NET 10, e.g. while parsing overload documentation). Documentation fragments are parsed rarely (completion
/// and overload help), so the per-parse allocation is negligible; holding no state also makes this safe to call
/// re-entrantly or concurrently.
/// </remarks>
internal sealed class XmlFragmentParser
{
    private static readonly XmlReaderSettings s_xmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
    };

    /// <summary>
    /// Parse the given XML fragment. The given callback is executed until either the end of the fragment
    /// is reached or an exception occurs.
    /// </summary>
    /// <typeparam name="TArg">Type of an additional argument passed to the <paramref name="callback"/> delegate.</typeparam>
    /// <param name="xmlFragment">The fragment to parse.</param>
    /// <param name="callback">Action to execute while there is still more to read.</param>
    /// <param name="arg">Additional argument passed to the callback.</param>
    /// <remarks>
    /// It is important that the <paramref name="callback"/> action advances the <see cref="XmlReader"/>,
    /// otherwise parsing will never complete.
    /// </remarks>
    public void ParseFragment<TArg>(string xmlFragment, Action<XmlReader, TArg> callback, TArg arg)
    {
        var textReader = new Reader(xmlFragment);
        using var xmlReader = XmlReader.Create(textReader, s_xmlSettings);

        while (!ReachedEnd(xmlReader))
        {
            if (BeforeStart(xmlReader))
            {
                // Skip over the synthetic root element and first node
                xmlReader.Read();
            }
            else
            {
                callback(xmlReader, arg);
            }
        }

        // Read the final EndElement to complete the synthetic current element.
        xmlReader.ReadEndElement();
    }

    private static bool BeforeStart(XmlReader xmlReader)
    {
        // Depth 0 = Document root
        // Depth 1 = Synthetic wrapper, "CurrentElement"
        // Depth 2 = Start of user's fragment.
        return xmlReader.Depth < 2;
    }

    private static bool ReachedEnd(XmlReader xmlReader)
    {
        return xmlReader.Depth == 1
            && xmlReader.NodeType == XmlNodeType.EndElement
            && xmlReader.LocalName == Reader.CurrentElementName;
    }

    /// <summary>
    /// A text reader over a synthesized XML stream consisting of a single root element wrapping the supplied
    /// fragment, followed by an effectively infinite stream of whitespace. A new instance is created for each
    /// parse, so it carries no state between parses.
    /// </summary>
    private sealed class Reader : TextReader
    {
        private readonly string _text;
        private int _position;

        // Base the root element name on a GUID to avoid accidental (or intentional) collisions. An underscore is
        // prefixed because element names must not start with a number.
        private static readonly string s_rootElementName = "_" + Guid.NewGuid().ToString("N");

        // We insert an extra synthetic element name to allow for raw text at the root
        internal static readonly string CurrentElementName = "_" + Guid.NewGuid().ToString("N");

        private static readonly string s_rootStart = "<" + s_rootElementName + ">";
        private static readonly string s_currentStart = "<" + CurrentElementName + ">";
        private static readonly string s_currentEnd = "</" + CurrentElementName + ">";

        public Reader(string text)
        {
            _text = text;
        }

        public override int Read(char[] buffer, int index, int count)
        {
            if (count == 0)
            {
                return 0;
            }

            // The stream synthesizes an XML document with:
            // 1. A root element start tag
            // 2. Current element start tag
            // 3. The user text (xml fragments)
            // 4. Current element end tag

            var initialCount = count;

            // <root>
            _position += EncodeAndAdvance(s_rootStart, _position, buffer, ref index, ref count);

            // <current>
            _position += EncodeAndAdvance(s_currentStart, _position - s_rootStart.Length, buffer, ref index, ref count);

            // text
            _position += EncodeAndAdvance(_text, _position - s_rootStart.Length - s_currentStart.Length, buffer, ref index, ref count);

            // </current>
            _position += EncodeAndAdvance(s_currentEnd, _position - s_rootStart.Length - s_currentStart.Length - _text.Length, buffer, ref index, ref count);

            // Pretend that the stream is infinite, i.e. never return 0 characters read.
            if (initialCount == count)
            {
                buffer[index++] = ' ';
                count--;
            }

            return initialCount - count;
        }

        private static int EncodeAndAdvance(string src, int srcIndex, char[] dest, ref int destIndex, ref int destCount)
        {
            if (destCount == 0 || srcIndex < 0 || srcIndex >= src.Length)
            {
                return 0;
            }

            var charCount = Math.Min(src.Length - srcIndex, destCount);
            Debug.Assert(charCount > 0);
            src.CopyTo(srcIndex, dest, destIndex, charCount);

            destIndex += charCount;
            destCount -= charCount;
            Debug.Assert(destCount >= 0);
            return charCount;
        }

        public override int Read()
        {
            // XmlReader does not call this API
            throw new NotSupportedException();
        }

        public override int Peek()
        {
            // XmlReader does not call this API
            throw new NotSupportedException();
        }
    }
}
