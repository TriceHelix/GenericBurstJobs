using System;

[assembly: TriceHelix.GenericBurstJobs.DisableGenericBurstJobRegistry]

namespace TriceHelix.GenericBurstJobs
{
    /// <summary>
    /// This attribute should be added to any assemblies that contain or reference generic Burst compiled jobs.
    /// The job structs and their generic arguments must be public in order to be registered.
    /// </summary>
    /// <remarks>
    /// Assemblies with the following names are automatically analyzed for convenience:
    /// <list type="bullet">
    /// <item>"Assembly-CSharp"</item>
    /// <item>"Assembly-CSharp-Editor"</item>
    /// </list>
    /// You can override this behaviour by utilizing <see cref="DisableGenericBurstJobRegistryAttribute"/>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class ContainsGenericBurstJobsAttribute : Attribute { }


    /// <summary>
    /// This attribute should be used to selectively exclude certain job structs from analysis.
    /// </summary>
    /// <remarks>
    /// When used on an assembly, it completely overrides <see cref="ContainsGenericBurstJobsAttribute"/>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class DisableGenericBurstJobRegistryAttribute : Attribute { }
}
