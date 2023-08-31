using Mono.Cecil;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine.Assertions;

namespace TriceHelix.GenericBurstJobs.Editor
{
    /// <summary>
    /// An inheritance tree for methods which connects abstract/virtual/interface methods to their overrides/implementations and vice versa.
    /// </summary>
    internal sealed class MethodImplTree
    {
        private readonly Dictionary<string, MethodDefinition> MethodBaseMapping;
        private readonly ImmutableMultiDictionary<string, MethodDefinition> MethodOverrideMapping;


        internal MethodImplTree(HashSet<string> targetAssemblySet, MethodDefinition[] elementalMethods)
        {
            Assert.IsTrue(targetAssemblySet != null);
            Assert.IsTrue(elementalMethods != null);

            List<(MethodDefinition m, MethodDefinition b)> methodBasePairs = new(4096);

            foreach (var mdef in elementalMethods)
            {
                // skip roots
                TypeDefinition declTdef = mdef.DeclaringType;
                if (declTdef.IsInterface || mdef.IsAbstract)
                    continue;

                string mname = mdef.Name;
                int gpc = mdef.HasGenericParameters ? mdef.GenericParameters.Count : 0;

                if (mdef.IsVirtual || mdef.IsFinal)
                {
                    // try to find base method
                    TypeReference baseTref = mdef.DeclaringType.BaseType?.GetElementType();
                    if (baseTref != null && baseTref.IsTargetType(targetAssemblySet)) // require non-external base type
                    {
                        if (baseTref.TryResolve(out TypeDefinition baseTdef) && baseTdef.HasMethods)
                        {
                            foreach (var pm in baseTdef.Methods)
                            {
                                if (Consider(pm))
                                    break;
                            }
                        }
                    }
                }

                // try to find interface method(s)
                if (declTdef.HasInterfaces)
                {
                    foreach (var iTref in declTdef.Interfaces.Select(impl => impl.InterfaceType.GetElementType()))
                    {
                        // no external interfaces
                        if (!iTref.IsTargetType(targetAssemblySet))
                            continue;

                        if (iTref.TryResolve(out TypeDefinition iTdef) && iTdef.HasMethods)
                        {
                            foreach (var iMdef in iTdef.Methods)
                            {
                                if (Consider(iMdef))
                                    break;
                            }
                        }
                    }
                }
                

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                bool Consider(MethodDefinition candidate)
                {
                    bool matches = candidate.Name == mname && gpc == (candidate.HasGenericParameters ? candidate.GenericParameters.Count : 0);
                    if (matches) methodBasePairs.Add((m: mdef, b: candidate));
                    return matches;
                }
            }

            MethodBaseMapping = new Dictionary<string, MethodDefinition>(methodBasePairs.Count);
            KeyValuePair<string, MethodDefinition>[] methodToOverrideArray = new KeyValuePair<string, MethodDefinition>[methodBasePairs.Count];

            int i = 0;
            foreach (var (m, b) in methodBasePairs)
            {
                MethodBaseMapping.Add(m.GetNormalizedName(), b);
                methodToOverrideArray[i] = new KeyValuePair<string, MethodDefinition>(b.GetNormalizedName(), m);
                i++;
            }

            MethodOverrideMapping = new ImmutableMultiDictionary<string, MethodDefinition>(methodToOverrideArray);
        }


        internal bool TryGetBaseMethod(MethodReference method, out MethodDefinition mdef)
        {
            if (method == null)
            {
                mdef = null;
                return false;
            }

            return MethodBaseMapping.TryGetValue(method.GetNormalizedName(), out mdef);
        }


        internal IEnumerable<MethodDefinition> IterateOverridesAndImplementations(MethodReference method)
        {
            return method != null
                ? MethodOverrideMapping.GetValuesForKey(method.GetNormalizedName())
                : Enumerable.Empty<MethodDefinition>();
        }
    }
}
