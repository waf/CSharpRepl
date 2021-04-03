using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSharpLangRepl
{
    static class RoslynExtensions
    {
        public static Solution ApplyChanges(this Solution edit, Workspace workspace)
        {
            if(!workspace.TryApplyChanges(edit))
            {
                throw new InvalidOperationException("Failed to apply edit to workspace");
            }
            return workspace.CurrentSolution;
        }
    }
}
