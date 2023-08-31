using System;
using System.Runtime.CompilerServices;
using UnityEditor;

namespace TriceHelix.GenericBurstJobs.Editor
{
    /// <summary>
    /// Public GenericBurstJobs code generation API.
    /// </summary>
    public static class CodeGen
    {
        /// <summary>
        /// Manually trigger project analysis and code generation. The resulting C# code will be written to <paramref name="outputPath"/>.
        /// This function will (re-)import the generated file if the destination is within the project's "Assets" folder.
        /// </summary>
        /// <param name="outputPath">
        /// If the provided file path is not absolute (rooted), it will be interpreted as relative to <see cref="UnityEngine.Application.dataPath"/> (<![CDATA[<]]>ProjectDir<![CDATA[>]]>/Assets).
        /// </param>
        public static void Activate(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Invalid Output File Path");

            outputPath = Utils.GetRootedScriptOutputPath(outputPath, ".cs", true);
            string[] resolvedTypeStrings = ProjectAnalysis.ResolveGenericJobTypes(out int numUniqueJobs);
            CodeHelper.WriteRegistryScript(resolvedTypeStrings, numUniqueJobs, outputPath);
        }


        /// <summary>
        /// Manually trigger project analysis and code generation. The resulting C# code will be written to the path specified in the configuration GUI or API.
        /// This function will (re-)import the generated file if the destination is within the project's "Assets" folder.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Activate()
        {
            Activate(GenericBurstJobsConfig.Global.OutputScriptPath);
        }


        /// <summary>
        /// This calls <see cref="Activate()"/> while displaying a progress bar to prevent user input.
        /// </summary>
        public static void ActivateWithProgressBar()
        {
            try
            {
                EditorUtility.DisplayProgressBar(
                    "Generic Burst Jobs",
                    "Analyzing project, resolving job types, generating registry script...",
                    1f);

                Activate();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
