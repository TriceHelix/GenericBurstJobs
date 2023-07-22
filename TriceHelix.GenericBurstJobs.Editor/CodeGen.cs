using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine.Assertions;
using UnityCodeGen;

namespace TriceHelix.GenericBurstJobs.Editor
{
    [Generator]
    internal class CodeGen : ICodeGenerator
    {
        private static readonly string BurstCompileAttribute_FullName = typeof(BurstCompileAttribute).FullName;
        private static readonly string ContainsGenericBurstJobsAttribute_FullName = typeof(ContainsGenericBurstJobsAttribute).FullName;
        private static readonly string DisableGenericBurstJobRegistryAttribute_FullName = typeof(DisableGenericBurstJobRegistryAttribute).FullName;
        private static readonly string RegisterGenericJobTypeAttribute_FullName = typeof(RegisterGenericJobTypeAttribute).FullName;

        private static readonly HashSet<string> AutoIncludedTargetAssemblies = new(2)
        {
            "Assembly-CSharp",
            "Assembly-CSharp-Editor"
        };


        public void Execute(GeneratorContext context)
        {
            // load assemblies and types
            AssemblyDefinition[] relevantAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => IsTargetAssembly(a))
                .Select(a => AssemblyDefinition.ReadAssembly(a.Location))
                .ToArray();

            try
            {
                StringBuilder script = new(32768);
                script.AppendLine("// THIS IS AN AUTOMATICALLY GENERATED FILE CREATED BY TriceHelix.GenericBurstJobs");
                script.AppendLine("// PLEASE DO NOT EDIT THE FILE MANUALLY - RE-RUN THE SOURCE GENERATOR TO REFRESH IT OR TO FIX ANY ERRORS");
                script.AppendLine();

                // get all types
                TypeDefinition[] allTypes = relevantAssemblies
                    .SelectMany(a => a.MainModule.GetTypes().Skip(1))
                    .GroupBy(t => t.FullName)
                    .Select(g => g.First())
                    .ToArray();

                // get target types
                TypeDefinition[] targetTypes = allTypes.Where(t => IsTargetType(t)).ToArray();
                List<string> resolvedTypeStrings = new(256);

                // cache
                StringBuilder typeStringBuilder = new(1024);

                // resolve generic parameters of targets
                GenericResolver resolver = new(allTypes, 128);
                foreach (var type in targetTypes)
                {
                    if (!type.HasGenericParameters)
                        continue; // failsafe

                    // resolve
                    TypeReference[] result = new TypeReference[type.GenericParameters.Count];
                    resolver.Resolve(type);

                    // get each combination of types
                    for (int i = 0; i < resolver.ResultCount; i++)
                    {
                        // re-use array for results
                        resolver.GetResultNoAlloc(i, result);

                        // get correctly formatted type string
                        typeStringBuilder.Clear();
                        BuildResolvedTypeName(typeStringBuilder, type, result);
                        resolvedTypeStrings.Add(typeStringBuilder.ToString());
                    }
                }

                // register resolved types via attribute in script
                int numDistinctTypes = 0;
                foreach (string typeString in resolvedTypeStrings.Distinct())
                {
                    numDistinctTypes++;

                    script.Append("[assembly: ");
                    script.Append(RegisterGenericJobTypeAttribute_FullName);
                    script.Append("(typeof(");
                    script.Append(typeString);
                    script.Append("))]");
                    script.AppendLine();
                }

                // summary
                if (numDistinctTypes > 0) script.AppendLine();
                script.AppendLine("// SUMMARY:");
                script.Append($"// Resolved ");
                script.Append(numDistinctTypes.ToString());
                script.Append($" unique generic job type{(numDistinctTypes != 1 ? "s" : string.Empty)} from ");
                script.Append(targetTypes.Length.ToString());
                script.AppendLine($" job{(targetTypes.Length != 1 ? "s" : string.Empty)}.");
                script.Append("// Time of last source-regeneration: ");
                script.AppendLine(DateTime.Now.ToString());

                context.AddCode("GenericBurstJobs_Registry.cs", script.ToString());
            }
            finally
            {
                // unload assemblies
                foreach (var a in relevantAssemblies)
                    a.Dispose();
            }
        }


        private static bool IsTargetAssembly(Assembly assembly)
        {
            // cannot be dynamic
            if (assembly.IsDynamic)
                return false;

            string[] attributeTypes = assembly.CustomAttributes.Select(attr => attr.AttributeType.FullName).ToArray();

            // check if explicitly excluded
            if (attributeTypes.Contains(DisableGenericBurstJobRegistryAttribute_FullName))
                return false;

            // automatically target Unity's builtin assemblies
            string name = assembly.GetName().Name;
            if (AutoIncludedTargetAssemblies.Contains(name))
                return true;

            // check if explicitly included
            return attributeTypes.Contains(ContainsGenericBurstJobsAttribute_FullName);
        }


        private static bool IsTargetType(TypeDefinition type)
        {
            // must be accessible, generic, value type
            if (!((type.IsPublic || type.IsNestedPublic) && type.IsValueType && type.HasGenericParameters))
                return false;

            // TODO: check for IJob<...> interfaces?

            string[] attributeTypes = type.CustomAttributes.Select(attr => attr.AttributeType.FullName).ToArray();

            // check if type was explicitly excluded
            if (attributeTypes.Contains(DisableGenericBurstJobRegistryAttribute_FullName))
                return false;

            // must be burst compiled
            return attributeTypes.Contains(BurstCompileAttribute_FullName);
        }


        private static void BuildResolvedTypeName(StringBuilder sb, TypeReference baseType, ReadOnlySpan<TypeReference> genericArguments)
        {
            Assert.IsTrue(baseType != null);
            Assert.IsTrue(genericArguments != null);

            Stack<TypeReference> ts = new(1);

            // build hierarchy
            TypeReference t = baseType;
            while (t != null)
            {
                ts.Push(t);
                t = t.DeclaringType;
            }

            // namespace
            t = ts.Peek();
            bool skipDotFlag;
            if (string.IsNullOrWhiteSpace(t.Namespace))
            {
                skipDotFlag = true;
            }
            else
            {
                sb.Append(t.Namespace);
                skipDotFlag = false;
            }

            // type chain
            int argc = 0;
            while (ts.TryPop(out t))
            {
                if (!skipDotFlag) sb.Append('.');
                else skipDotFlag = false;

                int lim = t.Name.IndexOf('`');
                if (lim < 0) lim = t.Name.Length;
                sb.Append(t.Name[..lim]);

                int gpc = t.HasGenericParameters ? (t.GenericParameters.Count - argc) : 0;
                if (gpc > 0)
                {
                    AppendGenericArgs(sb, genericArguments.Slice(argc, gpc));
                    argc += gpc;
                }
            }
        }


        private static void AppendGenericArgs(StringBuilder sb, ReadOnlySpan<TypeReference> types)
        {
            sb.Append('<');

            bool isFirst = true;
            foreach (var type in types)
            {
                if (!isFirst) sb.Append(", ");
                isFirst = false;
                TypeReference[] genericArgs = type.IsGenericInstance ? (type as GenericInstanceType).GenericArguments.ToArray() : Array.Empty<TypeReference>();
                BuildResolvedTypeName(sb, type, genericArgs);
            }

            sb.Append('>');
        }
    }
}
