using Mono.Cecil;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace TriceHelix.GenericBurstJobs.Editor
{
    internal static class Extensions
    {
        internal static bool IsTargetType(this TypeReference tref, HashSet<string> targetAssemblySet)
        {
            if (tref.IsGenericParameter)
                return false;

            IMetadataScope scope = tref.Scope;
            MetadataScopeType scopeType = scope.MetadataScopeType;

            string assemblyFullName;
            switch (scopeType)
            {
                case MetadataScopeType.AssemblyNameReference:
                    assemblyFullName = (scope as AssemblyNameReference).Name;
                    break;

                case MetadataScopeType.ModuleReference or MetadataScopeType.ModuleDefinition:
                    assemblyFullName = (scope as ModuleReference).Name;
                    int extIndex = assemblyFullName.LastIndexOf('.');
                    if (extIndex >= 0) assemblyFullName = assemblyFullName[..extIndex];
                    break;

                default: return false;
            }

            return assemblyFullName != null && targetAssemblySet.Contains(assemblyFullName);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsTargetMethod(this MethodReference mref, HashSet<string> targetAssemblySet)
        {
            return mref.DeclaringType.IsTargetType(targetAssemblySet);
        }


        internal static bool TryResolve(this TypeReference tref, out TypeDefinition tdef)
        {
            try
            {
                tdef = tref.Resolve();
                return tdef != null;
            }
            catch (AssemblyResolutionException ex)
            {
                UnityEngine.Debug.LogWarning($"Type \"{tref.FullName}\" could not be resolved. Underlying exception:");
                UnityEngine.Debug.LogException(ex);
                tdef = null;
                return false;
            }
        }


        internal static bool TryResolve(this MethodReference mref, out MethodDefinition mdef)
        {
            try
            {
                mdef = mref.Resolve();
                return true;
            }
            catch (AssemblyResolutionException ex)
            {
                UnityEngine.Debug.LogWarning($"Method \"{mref.FullName}\" could not be resolved. Underlying exception:");
                UnityEngine.Debug.LogException(ex);
                mdef = null;
                return false;
            }
        }


        internal static string GetNormalizedName(this TypeReference type, bool includeGenericArgs = false)
        {
            if (type.IsArray)
            {
                ArrayType at = (ArrayType)type;
                string suffix = at.Rank > 1 ? $"[{new string(',', at.Rank - 1)}]" : "[]";
                return type.GetElementType().GetNormalizedName(includeGenericArgs) + suffix;
            }

            if (type.IsPointer)
            {
                return type.GetElementType().GetNormalizedName(includeGenericArgs) + "*";
            }

            if (type.IsByReference)
            {
                return type.GetElementType().GetNormalizedName(includeGenericArgs) + "&";
            }

            if (type.IsFunctionPointer)
            {
                return "_FuncPtr_"; // normalization not supported yet
            }

            if (type.IsGenericParameter)
            {
                GenericParameter gp = (GenericParameter)type;
                return (gp.Type == GenericParameterType.Method ? "!!" : "!") + gp.Position;
            }

            string name = NormalizedNameBase(type);

            if (includeGenericArgs && type.IsGenericInstance)
            {
                GenericInstanceType git = (GenericInstanceType)type;
                if (git.HasGenericArguments)
                {
                    int numArgs = git.GenericArguments.Count;
                    StringBuilder sb = new(name.Length + (numArgs * 16));
                    sb.Append(name);

                    sb.Append('<');
                    for (int i = 0; i < numArgs; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(git.GenericArguments[i].GetNormalizedName(true));
                    }
                    sb.Append('>');

                    name = sb.ToString();
                }
            }

            return name;
            
            static string NormalizedNameBase(TypeReference type)
            {
                string name;

                if (type.IsNested)
                {
                    name = NormalizedNameBase(type.DeclaringType) + "/" + type.Name;
                }
                else
                {
                    name = string.IsNullOrEmpty(type.Namespace)
                        ? type.Name
                        : (type.Namespace + "." + type.Name);
                }

                return name;
            }
        }


        internal static string GetNormalizedName(this MethodReference method, bool includeGenericArgs = false)
        {
            StringBuilder sb = new(32);

            sb.Append(method.ReturnType.GetNormalizedName(true));
            sb.Append(' ');
            sb.Append(method.DeclaringType.GetNormalizedName(false));
            sb.Append("::");
            sb.Append(method.Name);
            if (method.HasGenericParameters)
            {
                sb.Append('`');
                sb.Append(method.GenericParameters.Count);
            }
            else if (method.IsGenericInstance)
            {
                GenericInstanceMethod gim = (GenericInstanceMethod)method;
                if (gim.HasGenericArguments)
                {
                    if (includeGenericArgs)
                    {
                        sb.Append('<');
                        for (int i = 0; i < gim.GenericArguments.Count; i++)
                        {
                            if (i > 0) sb.Append(' ');
                            sb.Append(gim.GenericArguments[i].GetNormalizedName(true));
                        }
                        sb.Append('>');
                    }
                    else
                    {
                        sb.Append('`');
                        sb.Append(gim.GenericArguments.Count);
                    }
                }
            }
            sb.Append('(');
            if (method.HasParameters)
            {
                for (int i = 0; i < method.Parameters.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    TypeReference ptype = method.Parameters[i].ParameterType;
                    sb.Append(ptype.GetNormalizedName(true));
                    if (ptype.IsSentinel) sb.Append("...");
                }
            }
            sb.Append(')');

            return sb.ToString();
        }
    }
}
