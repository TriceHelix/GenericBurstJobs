# Generic Burst Jobs in Unity

### Support for generic, Burst compiled Jobs through automatic code analysis and generation
Requires Unity 2020.3 LTS or newer.

---

#### Installation (via Package Manager)
* Select "Add package from git URL..."
* Enter `https://github.com/TriceHelix/GenericBurstJobs.git#upm` and click "Add"
* Done!

Additional Unity dependencies (these will be automatically installed):
* Unity.Burst (1.8 +)
* Unity.Collections (1.5 +)
* Mono.Cecil (1.11 +)
* Newtonsoft.Json (3.2 +)

---

#### Usage

The settings are located at `Project Settings > Generic Burst Jobs`. There you can manually run the source generator, change its output destination, and more. By default, the generator will run automatically when the Player application is built.

During code analysis, any job structs marked with `Unity.Jobs.BurstCompileAttribute` that contain generic parameters will be tracked. For each unique instance of these structs the source generator creates a corresponding `Unity.Jobs.RegisterGenericJobTypeAttribute` in the output script. It works even through several layers of generic parameters, where some may belong to a class and some to a method. As long as all generic arguments of the job structs can be inferred at compile time, they will be registered.

The code analyzer ignores all code that is not part of [Unity's default scripting assemblies](https://docs.unity3d.com/Manual/ScriptCompileOrderFolders.html). To enable analysis of an external assembly, simply add this attribute to one of its scripts: `[assembly: TriceHelix.GenericBurstJobs.ContainsGenericBurstJobs]`

You can also exclude specific job structs or analysis of the default assemblies by utilizing `TriceHelix.GenericBurstJobs.DisableGenericJobRegistryAttribute`.

---

#### Limitations
* Your generic job structs, aswell as any types which the struct is nested in, must be marked as `public`. Generic arguments for these jobs naturally have the same requirement.
* Analysis is affected by code stripping. If the compiler omits sections of code that contain references to generic job structs, they will not be registered. This is because the tool analyzes the compiled IL instead of your source code, rendering them undetectable.
* This tool is far from perfect and may not always provide flawless results. However, well written code will usually not be affected by this. If you have suggestions on how to improve code analysis, encounter a bug, or have any questions, feel free to open an issue or pull request!
