namespace CSharpRepl.Services.Nuget
{
    public class RuntimeConfigJson
    {
        public RuntimeOptions runtimeOptions { get; set; }
    }

    public class RuntimeOptions
    {
        public string tfm { get; set; }
        public Framework framework { get; set; }
    }

    public class Framework
    {
        public string name { get; set; }
        public string version { get; set; }
    }
}
