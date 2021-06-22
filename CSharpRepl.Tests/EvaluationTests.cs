// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests
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

        public Task InitializeAsync() => services.WarmUpAsync(Array.Empty<string>());
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
        public async Task Evaluate_NugetPackageVersioned_InstallsPackageVersion()
        {
            var installation = await services.Evaluate(@"#r ""nuget:Newtonsoft.Json, 12.0.1""");
            var usage = await services.Evaluate(@"Newtonsoft.Json.JsonConvert.SerializeObject(new { Foo = ""bar"" })");

            var installationResult = Assert.IsType<EvaluationResult.Success>(installation);
            var usageResult = Assert.IsType<EvaluationResult.Success>(usage);

            Assert.Null(installationResult.ReturnValue);
            Assert.Contains(installationResult.References, r => r.Display.EndsWith("Newtonsoft.Json.dll") && r.Display.Contains("Newtonsoft.Json.12.0.1"));
            Assert.Contains("Adding references for Newtonsoft.Json", stdout.ToString());
            Assert.Equal(@"{""Foo"":""bar""}", usageResult.ReturnValue);
        }

        [Fact]
        public async Task Evaluate_RelativeAssemblyReference_CanReferenceAssembly()
        {
            var referenceResult = await services.Evaluate(@"#r ""./Data/DemoLibrary.dll""");
            var importResult = await services.Evaluate("using DemoLibrary;");
            var multiplyResult = await services.Evaluate("DemoClass.Multiply(5, 6)");

            Assert.IsType<EvaluationResult.Success>(referenceResult);
            Assert.IsType<EvaluationResult.Success>(importResult);
            var successfulResult = Assert.IsType<EvaluationResult.Success>(multiplyResult);
            Assert.Equal(30, successfulResult.ReturnValue);
        }

        [Fact]
        public async Task Evaluate_AbsoluteAssemblyReference_CanReferenceAssembly()
        {
            var absolutePath = Path.GetFullPath("./Data/DemoLibrary.dll");
            var referenceResult = await services.Evaluate(@$"#r ""{absolutePath}""");
            var importResult = await services.Evaluate("using DemoLibrary;");
            var multiplyResult = await services.Evaluate("DemoClass.Multiply(7, 6)");

            Assert.IsType<EvaluationResult.Success>(referenceResult);
            Assert.IsType<EvaluationResult.Success>(importResult);
            var successfulResult = Assert.IsType<EvaluationResult.Success>(multiplyResult);
            Assert.Equal(42, successfulResult.ReturnValue);
        }

        [Fact]
        public async Task Evaluate_AssemblyReferenceInSearchPath_CanReferenceAssembly()
        {
            var referenceResult = await services.Evaluate(@"#r ""System.Linq.dll""");

            Assert.IsType<EvaluationResult.Success>(referenceResult);
        }

        [Fact]
        public async Task Evaluate_AssemblyReferenceWithSharedFramework_ReferencesSharedFramework()
        {
            var referenceResult = await services.Evaluate(@"#r ""./Data/WebApplication1.dll""");
            var sharedFrameworkResult = await services.Evaluate(@"using Microsoft.AspNetCore.Hosting;");
            var applicationResult = await services.Evaluate(@"using WebApplication1;");

            Assert.IsType<EvaluationResult.Success>(referenceResult);
            Assert.IsType<EvaluationResult.Success>(sharedFrameworkResult);
            Assert.IsType<EvaluationResult.Success>(applicationResult);
        }

        [Fact]
        public async Task Evaluate_ProjectReference_ReferencesProject()
        {
            var referenceResult = await services.Evaluate(@"#r ""./../../../../CSharpRepl.Services/CSharpRepl.Services.csproj""");
            var importResult = await services.Evaluate(@"using CSharpRepl.Services;");

            Assert.IsType<EvaluationResult.Success>(referenceResult);
            Assert.IsType<EvaluationResult.Success>(importResult);
        }
    }
}
