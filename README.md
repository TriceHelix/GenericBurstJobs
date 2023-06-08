# Generic Burst Jobs for Unity

### Support for generic, Burst compiled Job structs through automatic code analysis and generation
Requires Unity 2020.3 or newer.

---

#### Installation (via Package Manager)
* Click "Add package from git URL..."
* Enter `https://github.com/TriceHelix/GenericBurstJobs.git#upm` and click "Add"
* Done!

*NOTE: This will install the following dependencies:*
* Unity.Burst
* Unity.Collections
* A fork of https://github.com/AnnulusGames/UnityCodeGen

Mono.Cecil is also required but already built into the Unity Editor.

---

#### Usage

By default, any job structs containing generic parameters and marked with `[Unity.Jobs.BurstCompile]` will be tracked. Whenever you specify generic parameters in your code, these arguments will be propagated to your generic job structs and a corresponding `[Unity.Jobs.RegisterGenericJobType]` attribute will be added to a generated script.

When using seperate assemblies (not the default "Assembly-CSharp"), you must mark them by adding the following to one of its scripts: `[assembly: TriceHelix.GenericBurstJobs.ContainsGenericBurstJobs]`

You can exclude specific job structs or entire assemblies by utilizing this attribute: `[TriceHelix.GenericBurstJobs.DisableGenericJobRegistry]`

Code generation is managed by [this dependency](https://github.com/AnnulusGames/UnityCodeGen). The easiest way to ensure the code will be refreshed when changes are made is to enable source re-generation before every recompile. This option is located at `Tools > UnityCodeGen > Auto-generate on recompile`. Alternatively, you can manually refresh it via `Tools > UnityCodeGen > Generate`.

---

### Limitations
* Your generic job structs, aswell as any types which the struct is nested in, must be marked as `public`.
* Generic type arguments must be known at compile time, since Burst only compiles your code AOT (ahead of time, within the Unity Editor).
* Code analysis is susceptible to code stripping. For example, a local instance of a job which is never used/referenced will be omitted from the compiled IL, thus making it impossible to recognize for this tool.
