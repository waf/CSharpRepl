// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services.Logging;
using System;

namespace CSharpRepl.Logging
{
    internal sealed class NullLogger : ITraceLogger
    {
        public void Log(string message) { /* null logger does not log */ }
        public void Log(Func<string> message) { /* null logger does not log */ }
    }
}
