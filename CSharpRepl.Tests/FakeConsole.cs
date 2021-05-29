using NSubstitute;
using PrettyPrompt.Consoles;
using System.Text;

namespace Sharply.Tests
{
    static class FakeConsole
    {
        public static (IConsole console, StringBuilder stdout, StringBuilder stderr) CreateStubbedOutputAndError()
        {
            var stub = Substitute.For<IConsole>();
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            stub.When(c => c.Write(Arg.Any<string>())).Do(args => stdout.Append(args.Arg<string>()));
            stub.When(c => c.WriteLine(Arg.Any<string>())).Do(args => stdout.AppendLine(args.Arg<string>()));
            stub.When(c => c.WriteError(Arg.Any<string>())).Do(args => stderr.Append(args.Arg<string>()));
            stub.When(c => c.WriteErrorLine(Arg.Any<string>())).Do(args => stderr.AppendLine(args.Arg<string>()));
            return (stub, stdout, stderr);
        }
        public static (IConsole console, StringBuilder stdout) CreateStubbedOutput()
        {
            var stub = Substitute.For<IConsole>();
            var stdout = new StringBuilder();
            stub.When(c => c.Write(Arg.Any<string>())).Do(args => stdout.Append(args.Arg<string>()));
            stub.When(c => c.WriteLine(Arg.Any<string>())).Do(args => stdout.AppendLine(args.Arg<string>()));
            return (stub, stdout);
        }
    }
}
