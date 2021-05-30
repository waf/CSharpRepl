// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Sharply.Services;
using Sharply.Services.Roslyn;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Sharply.Tests
{
    [Collection(nameof(RoslynServices))]
    public class EvaluationTests : IAsyncLifetime
    {
        private readonly RoslynServices services;
        private readonly StringBuilder stdout;

        public EvaluationTests()
        {
            var (console, stdout) = FakeConsole.CreateStubbedOutput();
            this.services = new RoslynServices(console, new Configuration());
            this.stdout = stdout;
        }

        public Task InitializeAsync() => services.WarmUpAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Evaluate_LiteralInteger_ReturnsInteger()
        {
            var result = await services.Evaluate("5");

            var success = Assert.IsType<EvaluationResult.Success>(result);
            Assert.Equal("5", success.Input);

            var returnValue = Assert.IsType<int>(success.ReturnValue);
            Assert.Equal(5, returnValue);
        }

        [Fact]
        public async Task Evaluate_Variable_ReturnsValue()
        {
            var variableAssignment = await services.Evaluate(@"var x = ""Hello World"";");
            var variableUsage = await services.Evaluate(@"x.Replace(""World"", ""Mundo"")");

            var assignment = Assert.IsType<EvaluationResult.Success>(variableAssignment);
            var usage = Assert.IsType<EvaluationResult.Success>(variableUsage);
            Assert.Null(assignment.ReturnValue);
            Assert.Equal("Hello Mundo", usage.ReturnValue);
        }

        [Fact]
        public async Task Evaluate_NugetPackage_InstallsPackage()
        {
            var installation = await services.Evaluate(@"#r ""nuget:Newtonsoft.Json""");
            var usage = await services.Evaluate(@"Newtonsoft.Json.JsonConvert.SerializeObject(new { Foo = ""bar"" })");

            var installationResult = Assert.IsType<EvaluationResult.Success>(installation);
            var usageResult = Assert.IsType<EvaluationResult.Success>(usage);

            Assert.Null(installationResult.ReturnValue);
            Assert.Contains(installationResult.References, r => r.Display.EndsWith("Newtonsoft.Json.dll"));
            Assert.Contains("Adding references for Newtonsoft.Json", stdout.ToString());
            Assert.Equal(@"{""Foo"":""bar""}", usageResult.ReturnValue);
        }

        [Fact]
        public async Task Evaluate_NugetPackageDoesNotExist_PrintsError()
        {
            string missingNugetPackage = Guid.NewGuid().ToString();
            var installation = await services.Evaluate(@$"#r ""nuget:{missingNugetPackage}""");

            var installationResult = Assert.IsType<EvaluationResult.Error>(installation);

            Assert.Equal($@"Could not find package ""{missingNugetPackage}""", installationResult.Exception.Message);
        }
    }
}
