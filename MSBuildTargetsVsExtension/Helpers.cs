using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSBuildTargetsVsExtension
{
    public static class Helpers
    {
        public static bool IsPathSubpathOf(string parentPath, string childPath)
        {
            var pp = Path.GetFullPath(parentPath);
            var cp = Path.GetFullPath(childPath);
            return cp.StartsWith(pp, StringComparison.OrdinalIgnoreCase);
        }
    }
}
