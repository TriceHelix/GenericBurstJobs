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
                    GenericBurstJobsConfig config = JsonConvert.DeserializeObject<GenericBurstJobsConfig>(File.ReadAllText(ConfigFilePath));
                    isDefault = false;
                    return config;
                }
                catch { }
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
                    keywords: new HashSet<string>() { "Generic", "Burst", "Jobs", "Analysis", "Code", "Generation", "Trice", "Helix" }
                    );

                return provider;
            }


            private CustomSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null) : base(path, scopes, keywords)
            {
                label = "Generic Burst Jobs";
            }


            private static readonly GUIContent outputPathLabel = new(
                "Output Script Path",
                "The generated script which registers your generic job types with Burst is saved to this location. " +
                "It is relative to the \"Assets\" folder in the project."
                );

            private static readonly GUIContent buildEventOrderLabel = new(
                "Build Event Order",
                "Value used by Unity to determine when to generate the registry script when building the Player. " +
                "If you don't have any custom build events which depend on all scripts being available and up-to-date, leave this value at 0."
                );

            private static readonly GUIContent activateOnRecompileLabel = new(
                "Activate On Recompile",
                "When enabled, the registry script is regenerated after every recompilation. " +
                "This will fix errors caused by the removal or renaming of types automatically, but at the cost of more overhead with every tiem you modify your scripts. " +
                "It is recommended you leave this option disabled and instead manually trigger regeneration using the button below."
                );

            private static readonly GUIContent activateCodegenButton = new(
                "Activate Code Generation",
                "Manually trigger a regeneration of the registry script for Generic Burst Jobs."
                );


            public override void OnGUI(string searchContext)
            {
                EditorGUILayout.Space();
                GUILayout.Label("Settings", EditorStyles.boldLabel);

                Global.OutputScriptPath = EditorGUILayout.DelayedTextField(outputPathLabel, Global.OutputScriptPath);
                Global.BuildEventOrder = EditorGUILayout.DelayedIntField(buildEventOrderLabel, Global.BuildEventOrder);
                Global.ActivateOnRecompile = EditorGUILayout.Toggle(activateOnRecompileLabel, Global.ActivateOnRecompile);

                EditorGUILayout.Space();
                GUILayout.Label("Actions");

                if (GUILayout.Button(activateCodegenButton, GUILayout.Height(24f)))
                {
                    CodeGen.ActivateWithProgressBar();
                }

                EditorGUILayout.Space(0f, true);
            }


            public override void OnDeactivate()
            {
                Global?.Save();
            }
        }
        
    }
}
