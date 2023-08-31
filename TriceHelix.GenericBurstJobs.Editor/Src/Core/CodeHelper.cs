using System;
using System.IO;
using System.Text;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;

namespace TriceHelix.GenericBurstJobs.Editor
{
    internal static class CodeHelper
    {
        private static readonly string RegisterGenericJobTypeAttribute_FullName = typeof(RegisterGenericJobTypeAttribute).FullName;


        internal static string GenerateRegistryScript(string[] resolvedTypeStrings, int numUniqueJobs)
        {
            Assert.IsTrue(resolvedTypeStrings != null);
            Assert.IsTrue(numUniqueJobs >= 0);

            StringBuilder script = new(16384);
            int numResolvedTypes = resolvedTypeStrings.Length;

            // header
            script.AppendLine("// THIS IS AN AUTOMATICALLY GENERATED FILE CREATED BY TriceHelix.GenericBurstJobs");
            script.AppendLine("// PLEASE DO NOT EDIT THE FILE MANUALLY - RUN THE SOURCE GENERATOR TO REFRESH IT OR TO FIX ANY ERRORS");
            script.AppendLine("// YOU SHOULD DELETE THIS FILE AFTER CHANGING THE OUTPUT LOCATION IN THE SETTINGS");
            if (numResolvedTypes > 0) script.AppendLine();

            // attributes
            for (int i = 0; i < numResolvedTypes; i++)
            {
                script.AppendLine($"[assembly: {RegisterGenericJobTypeAttribute_FullName}(typeof({resolvedTypeStrings[i]}))]");
            }

            // summary
            DateTime time = DateTime.Now;
            script.AppendLine();
            script.AppendLine("// SUMMARY:");
            script.AppendLine($"// Resolved {numResolvedTypes} unique job type{(numResolvedTypes != 1 ? "s" : string.Empty)} from {numUniqueJobs} generic job struct{(numUniqueJobs != 1 ? "s" : string.Empty)}.");
            script.AppendLine($"// Generated on {time.ToShortDateString()} @ {time.ToLongTimeString()} in version {Application.version}");

            return script.ToString();
        }


        internal static void WriteRegistryScript(string[] resolvedTypeStrings, int numUniqueJobs, string filePath, bool import = true)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path is null or empty.");

            string script = GenerateRegistryScript(resolvedTypeStrings, numUniqueJobs);

            // write script to file
            string dir = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, script);

            if (import)
            {
                // import script asset if written into project
                string assetPath = Path.GetRelativePath(Path.GetDirectoryName(Application.dataPath), filePath);
                if (assetPath.StartsWith("Assets"))
                {
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                }
            }
        }
    }
}
