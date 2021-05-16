using System.Collections.Generic;

namespace Sharply.Services
{
    public class Configuration
    {
        public HashSet<string> References { get; } = new();
        public HashSet<string> Usings { get; } = new();
        public string Sdk { get; set; } = Roslyn.Sdk.NetCoreApp;
        public string Theme { get; set; }
        public string ResponseFile { get; set; }
        public string LoadScript { get; set; }
        public bool ShowVersionAndExit { get; set; }
        public bool ShowHelpAndExit { get; set; }
    }
}
