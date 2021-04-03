/*
using System;
using System.IO;
using System.Threading.Tasks;

namespace CSharpLangRepl
{
    /// <summary>
    /// A StringWriter than can detect the difference between empty string output vs never being written to.
    /// A normal StringWriter will return empty string in both scenarios.
    /// </summary>
    public class ConsoleOutputWriter : StringWriter
    {
        private bool hasOutput;
        private readonly TextWriter oldOutputStream;

        public string GetOutputOrNull() =>
            hasOutput ? GetStringBuilder().ToString() : null;

        public ConsoleOutputWriter(bool captureStandardOut = true)
        {
            if (captureStandardOut)
            {
                oldOutputStream = Console.Out;
                Console.SetOut(this);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if(oldOutputStream != null)
            {
                Console.SetOut(oldOutputStream);
            }
        }

        #region Write Overrides that set hasOutput

        public override void Write(char value)
        {
            hasOutput = true;
            base.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            hasOutput = true;
            base.Write(buffer, index, count);
        }

        public override void Write(string value)
        {
            hasOutput = true;
            base.Write(value);
        }

        public override Task WriteAsync(char value)
        {
            hasOutput = true;
            return base.WriteAsync(value);
        }

        public override Task WriteAsync(string value)
        {
            hasOutput = true;
            return base.WriteAsync(value);
        }

        public override Task WriteAsync(char[] buffer, int index, int count)
        {
            hasOutput = true;
            return base.WriteAsync(buffer, index, count);
        }

        public override Task WriteLineAsync(char value)
        {
            hasOutput = true;
            return base.WriteLineAsync(value);
        }

        public override Task WriteLineAsync(string value)
        {
            hasOutput = true;
            return base.WriteLineAsync(value);
        }

        public override Task WriteLineAsync(char[] buffer, int index, int count)
        {
            hasOutput = true;
            return base.WriteLineAsync(buffer, index, count);
        }

        #endregion
    }
}
*/
