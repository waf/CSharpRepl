// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace CSharpRepl.Services.Logging
{
    public interface ITraceLogger
    {
        void Log(string message);
        void Log(Func<string> message);
    }
}