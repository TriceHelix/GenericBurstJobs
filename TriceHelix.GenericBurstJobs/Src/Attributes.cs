using System;

[assembly: TriceHelix.GenericBurstJobs.DisableGenericBurstJobRegistry]

namespace TriceHelix.GenericBurstJobs
{
    /// <summary>
    /// This attribute should be added to any assemblies that contain or invoke generic Burst compiled jobs.
    /// </summary>
    /// <remarks>
    /// Generic job structs MUST BE PUBLIC in order to be visible to the code analyzer.
    /// Unity's default "Assembly-CSharp" and "Assembly-CSharp-Editor" assemblies are automatically analyzed and do not require this attribute.
    /// You can disable this behaviour by utilizing <see cref="DisableGenericBurstJobRegistryAttribute"/>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class ContainsGenericBurstJobsAttribute : Attribute { }


    /// <summary>
    /// This attribute disables automatic registering of Burst compiled jobs either for a single job or a whole assembly.
    /// It completely overrides <see cref="ContainsGenericBurstJobsAttribute"/> on the assembly or job itself.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class DisableGenericBurstJobRegistryAttribute : Attribute { }
}
