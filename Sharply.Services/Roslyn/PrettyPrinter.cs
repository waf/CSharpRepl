using Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sharply.Services.Roslyn
{
    class PrettyPrinter
    {
        private readonly ObjectFormatter formatter;
        private readonly PrintOptions summaryOptions;
        private readonly PrintOptions detailedOptions;

        public PrettyPrinter()
        {
            this.formatter = CSharpObjectFormatter.Instance;
            this.summaryOptions = new PrintOptions
            {
                MemberDisplayFormat = MemberDisplayFormat.SingleLine,
                MaximumOutputLength = 10_000,
            };
            this.detailedOptions = new PrintOptions
            {
                MemberDisplayFormat = MemberDisplayFormat.SeparateLines,
                MaximumOutputLength = 10_000,
            };
        }

        public string FormatObject(object obj, bool displayDetails) => obj is null
            ? null // intercept null, don't print the string "null"
            : formatter.FormatObject(obj, displayDetails ? detailedOptions : summaryOptions);
    }
}
