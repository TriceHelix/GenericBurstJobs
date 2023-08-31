using System.IO;
using UnityEngine;

namespace TriceHelix.GenericBurstJobs.Editor
{
    internal static class Utils
    {
        internal static string GetRootedScriptOutputPath(string path, string ext = ".cs", bool anyExtension = false)
        {
            if (!string.IsNullOrEmpty(path))
            {
                // add root
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(Application.dataPath, path);

                // fix extension
                if (ext != null)
                {
                    if ((anyExtension && !path.Contains('.')) || !path.EndsWith(ext))
                        path += ext;
                }
            }

            return path;
        }
    }
}
