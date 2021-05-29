using Sharply.Services;
using Sharply.Services.Roslyn;
using System;
using System.Linq;
using Xunit;

namespace Sharply.Tests
{
    public class CommandLineTests
    {
        [Fact]
        public void ParseArguments_NoArguments_ProducesDefaultConfiguration()
        {
            var result = Parse(null);
            Assert.NotNull(result);
            Assert.Equal(SharedFramework.NetCoreApp, result.Framework);
        }

        [Theory]
        [InlineData("-f"), InlineData("--framework"), InlineData("/f")]
        public void ParseArguments_FrameworkArgument_SpecifiesFramework(string flag)
        {
            var result = Parse($"{flag} Microsoft.AspNetCore.App");
            Assert.NotNull(result);
            Assert.Equal("Microsoft.AspNetCore.App", result.Framework);

            var concatenatedValue = Parse($"/f:Microsoft.AspNetCore.App");
            Assert.Equal("Microsoft.AspNetCore.App", concatenatedValue.Framework);
        }

        [Theory]
        [InlineData("-u"), InlineData("--usings"), InlineData("/u")]
        public void ParseArguments_UsingArguments_ProducesUsings(string flag)
        {
            var result = Parse($"{flag} System.Linq System.Data Newtonsoft.Json");
            Assert.NotNull(result);
            Assert.Equal(new[] { "System.Linq", "System.Data", "Newtonsoft.Json" }, result.Usings);

            var concatenatedValues = Parse($"/u:System.Linq /u:System.Data /u:Newtonsoft.Json");
            Assert.Equal(new[] { "System.Linq", "System.Data", "Newtonsoft.Json" }, concatenatedValues.Usings);
        }

        [Theory]
        [InlineData("-r"), InlineData("--references"), InlineData("/r")]
        public void ParseArguments_ReferencesArguments_ProducesUsings(string flag)
        {
            var result = Parse($"{flag} Foo.dll Bar.dll");
            Assert.NotNull(result);
            Assert.Equal(new[] { "Foo.dll", "Bar.dll" }, result.References);

            var concatenatedValues = Parse($"/r:Foo.dll /r:Bar.dll");
            Assert.Equal(new[] { "Foo.dll", "Bar.dll" }, concatenatedValues.References);
        }

        [Theory]
        [InlineData("-t"), InlineData("--theme"), InlineData("/t")]
        public void ParseArguments_ThemeArguments_SpecifiesTheme(string flag)
        {
            var result = Parse($"{flag} beautiful.json");
            Assert.NotNull(result);
            Assert.Equal("beautiful.json", result.Theme);
        }

        [Theory]
        [InlineData("-v"), InlineData("--version"), InlineData("/v")]
        public void ParseArguments_VersionArguments_SpecifiesVersion(string flag)
        {
            var result = Parse(flag);
            Assert.NotNull(result);
            Assert.True(result.ShowVersionAndExit);
        }

        [Theory]
        [InlineData("-h"), InlineData("--help"), InlineData("/h"), InlineData("/?")]
        public void ParseArguments_HelpArguments_SpecifiesHelp(string flag)
        {
            var result = Parse(flag);
            Assert.NotNull(result);
            Assert.True(result.ShowHelpAndExit);
        }

        [Fact]
        public void ParseArguments_ComplexCommandLine_ProducesConfiguration()
        {
            var result = Parse("-t foo.json -u System.Linq System.Data -u Newtonsoft.Json --reference foo.dll -f Microsoft.AspNetCore.App --reference bar.dll baz.dll Data/LoadScript.csx");
            Assert.NotNull(result);
            Assert.Equal("foo.json", result.Theme);
            Assert.Equal(new[] { "System.Linq", "System.Data", "Newtonsoft.Json" }, result.Usings);
            Assert.Equal(new[] { "foo.dll", "bar.dll", "baz.dll" }, result.References);
            Assert.Equal("Microsoft.AspNetCore.App", result.Framework);
            Assert.Equal(@"Console.WriteLine(""Hello World!"");", result.LoadScript);
        }

        [Fact]
        public void ParseArguments_ResponseFile_ProducesConfiguration()
        {
            var result = Parse("@Data/ResponseFile.rsp");

            Assert.NotNull(result);
            Assert.Equal(SharedFramework.NetCoreApp, result.Framework);
            Assert.Equal(new[] { "System", "System.Linq", "Foo.Main.Text" }, result.Usings);
            Assert.Equal(new[] { "System", "System.ValueTuple.dll", "Foo.Main.Logic.dll", "lib.dll" }, result.References);
        }

        private static Configuration Parse(string commandline) =>
            CommandLine.ParseArguments(commandline?.Split(' ') ?? Array.Empty<string>());
    }
}
