using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace TriceHelix.GenericBurstJobs.Editor
{
    [Serializable]
    [InitializeOnLoad]
    public sealed class GenericBurstJobsConfig
    {
        private static readonly string ConfigFilePath;
        public static readonly GenericBurstJobsConfig Global;
        
        static GenericBurstJobsConfig()
        {
            ConfigFilePath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "ProjectSettings", "GenericBurstJobsConfig.json");
            Global = Load();
        }


        [JsonProperty("output_path")]
        internal string OutputScriptPath;

        [JsonProperty("build_event_order")]
        internal int BuildEventOrder;

        [JsonProperty("activate_on_recompile")]
        internal bool ActivateOnRecompile;


        // DEFAULT CONFIG
        private GenericBurstJobsConfig()
        {
            OutputScriptPath = $"GENERATED{Path.DirectorySeparatorChar}GenericBurstJobsRegistry.cs";
            BuildEventOrder = 0;
            ActivateOnRecompile = false;
        }


        internal void Save()
        {
            string dir = Path.GetDirectoryName(ConfigFilePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(this, Formatting.None));
        }


        internal static GenericBurstJobsConfig Load(out bool isDefault)
        {
            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    // CUSTOM
                    string text = File.ReadAllText(ConfigFilePath);
                    if (string.IsNullOrEmpty(text)) throw new Exception("Empty config file");
                    GenericBurstJobsConfig config = JsonConvert.DeserializeObject<GenericBurstJobsConfig>(text);
                    isDefault = false;
                    return config;
                }
                catch
                {
                    Debug.LogWarning("Loading GenericBurstJobs configuration failed, using default settings.");
                }
            }

            // DEFAULT
            isDefault = true;
            return new GenericBurstJobsConfig();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static GenericBurstJobsConfig Load() => Load(out _);


        private sealed class CustomSettingsProvider : SettingsProvider
        {
            [SettingsProvider]
            internal static SettingsProvider CreateSettingsProvider()
            {
                CustomSettingsProvider provider = new(
                    path: "Project/Generic Burst Jobs",
                    scopes: SettingsScope.Project,
                    keywords: new HashSet<string>() { "Code", "Analysis", "Generation", "Trice", "Helix" }
                    );

                return provider;
            }


            private CustomSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null) : base(path, scopes, keywords)
            {
                label = "Generic Burst Jobs";
            }


            private static readonly GUIContent outputPathLabel = new(
                "Output Script Path",
                "The generated script which registers the project's generic job types with Burst is saved to this location. " +
                "When no absolute (rooted) path is provided, it is relative to the \"Assets\" folder."
                );

            private static readonly GUIContent buildEventOrderLabel = new(
                "Build Event Order",
                "Determines the order of events when building the Player application. " +
                "The lower the value, the earlier code generation is run in the build process. " +
                "Leave this at 0 as long as you don't have custom build events which might conflict with the source generator."
                );

            private static readonly GUIContent activateOnRecompileLabel = new(
                "Activate On Recompile",
                "When enabled, the source generator is run after every script recompilation. " +
                "This will fix potential errors caused by the removal or renaming of types automatically at the cost of more overhead for each recompilation. " +
                "It is recommended you leave this option disabled and instead manually run the generator using the button below when encountering issues. " +
                "The generator will always run when the Player application is built, nomatter the value of this option."
                );

            private static readonly GUIContent activateCodegenButton = new(
                "Activate Code Generation",
                "Manually regenerate the registry script for Generic Burst Jobs."
                );


            public override void OnGUI(string searchContext)
            {
                EditorGUIUtility.labelWidth = 160f;

                EditorGUILayout.Space();

                // settings
                int orgIndent = EditorGUI.indentLevel++;
                Global.OutputScriptPath = EditorGUILayout.DelayedTextField(outputPathLabel, Global.OutputScriptPath);
                Global.BuildEventOrder = EditorGUILayout.DelayedIntField(buildEventOrderLabel, Global.BuildEventOrder);
                Global.ActivateOnRecompile = EditorGUILayout.Toggle(activateOnRecompileLabel, Global.ActivateOnRecompile);
                EditorGUI.indentLevel = orgIndent;

                EditorGUILayout.Space();

                // actions
                GUILayout.BeginHorizontal();
                EditorGUILayout.Space(6f, false);
                bool activate = GUILayout.Button(activateCodegenButton, GUILayout.Height(24f), GUILayout.MaxWidth(400f));
                EditorGUILayout.Space(6f, true);
                GUILayout.EndHorizontal();

                if (activate)
                {
                    CodeGen.ActivateWithProgressBar();
                }

                EditorGUILayout.Space(0f, true);

                // reset label width
                EditorGUIUtility.labelWidth = 0f;
            }


            public override void OnDeactivate()
            {
                Global?.Save();
            }
        }
        
    }
}
