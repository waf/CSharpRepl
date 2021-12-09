// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace CSharpRepl.Services.SymbolExploration;

internal sealed class NullSymbolLogger : Microsoft.SymbolStore.ITracer
{
    public void WriteLine(string message) { }

    public void WriteLine(string format, params object[] arguments) { }

    public void Information(string message) { }

    public void Information(string format, params object[] arguments) { }

    public void Warning(string message) { }

    public void Warning(string format, params object[] arguments) { }

    public void Error(string message) { }

    public void Error(string format, params object[] arguments) { }

    public void Verbose(string message) { }

    public void Verbose(string format, params object[] arguments) { }
}
