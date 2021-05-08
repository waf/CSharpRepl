using Microsoft.CodeAnalysis;
using System;

namespace ReplDotNet.Roslyn
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
