#region License Header
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
#endregion

using System;

namespace CSharpRepl.Services.Completion.OpenAI;

/// <summary>
/// An exception that represents an error from the OpenAI API.
/// </summary>
internal class OpenAIException : Exception
{
    public OpenAIException() { }
    public OpenAIException(string? message) : base(message) { }
    public OpenAIException(string? message, Exception? innerException) : base(message, innerException) { }
}
