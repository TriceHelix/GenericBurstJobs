using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Burst;
using UnityEditor;
using UnityEngine.Assertions;

namespace TriceHelix.GenericBurstJobs.Editor
{
    internal static class ProjectAnalysis
    {
        private static readonly string BurstCompileAttribute_FullName = typeof(BurstCompileAttribute).FullName;
        private static readonly string ContainsGenericBurstJobsAttribute_FullName = typeof(ContainsGenericBurstJobsAttribute).FullName;
        private static readonly string DisableGenericBurstJobRegistryAttribute_FullName = typeof(DisableGenericBurstJobRegistryAttribute).FullName;
        private static readonly string TempAssemblyDir = Path.Combine(Path.GetTempPath(), "TriceHelix-GenericBurstJobs-TMP");

        private static readonly HashSet<string> AutoIncludedTargetAssemblies = new(2)
        {
            // assemblies with these names are automatically targeted for convenience (unless explicitly excluded)
            "Assembly-CSharp",
            "Assembly-CSharp-Editor",
            "Assembly-CSharp-firstpass",
            "Assembly-CSharp-Editor-firstpass"
        };


        [InitializeOnLoadMethod]
        private static void OnInit()
        {
            EditorApplication.quitting += OnEditorShutdown;
        }


        private static void OnEditorShutdown()
        {
            ClearTempAssemblies();
        }


        private static void ClearTempAssemblies()
        {
            if (Directory.Exists(TempAssemblyDir))
                Directory.Delete(TempAssemblyDir, true);
        }


        internal static string[] ResolveGenericJobTypes(out int numUniqueJobs)
        {
            List<string> resolvedTypeStrings = new(256);
            DefaultAssemblyResolver assemblyResolver = new();
            ReaderParameters readerParams = new(ReadingMode.Deferred)
            {
                AssemblyResolver = assemblyResolver,
                ReadWrite = false
            };

            /*
            // add .NET runtime libraries
            string netLibsDir = RuntimeEnvironment.GetRuntimeDirectory();
            assemblyResolver.AddSearchDirectory(netLibsDir);
            foreach (string dir in Directory.GetDirectories(netLibsDir, "*", SearchOption.AllDirectories))
                assemblyResolver.AddSearchDirectory(dir);

            // add Unity libraries
            string unityLibsDir = Path.Combine(Path.GetDirectoryName(EditorApplication.applicationPath), "Data", "Managed");
            assemblyResolver.AddSearchDirectory(unityLibsDir);
            foreach (string dir in Directory.GetDirectories(unityLibsDir, "*", SearchOption.AllDirectories))
                assemblyResolver.AddSearchDirectory(dir);
            */

            // create temporary storage for assembly copies
            ClearTempAssemblies();
            Directory.CreateDirectory(TempAssemblyDir);
            assemblyResolver.AddSearchDirectory(TempAssemblyDir);

            // copy targets to temp location (this avoids errors where Unity tries to access the assemblies when they are still opened for analysis)
            Assembly[] targetInfos = AppDomain.CurrentDomain.GetAssemblies().Where(a => IsTargetAssembly(a)).ToArray();
            int targetCount = targetInfos.Length;
            string[] targetPaths = new string[targetCount];
            for (int i = 0; i < targetCount; i++)
            {
                string src = targetInfos[i].Location;
                string dest = Path.Combine(TempAssemblyDir, Path.GetFileName(src));
                File.Copy(src, dest, true);
                targetPaths[i] = dest;
            }

            // load temp copies of assemblies
            AssemblyDefinition[] targetAssemblies = new AssemblyDefinition[targetCount];
            for (int i = 0; i < targetCount; i++)
            {
                targetAssemblies[i] = AssemblyDefinition.ReadAssembly(targetPaths[i], readerParams);
            }

            try
            {
                // get all elemental types (no generic arguments)
                TypeDefinition[] elementalTypes = targetAssemblies
                    .SelectMany(a => a.MainModule.GetTypes().Skip(1)) // always skip special first type
                    .GroupBy(tdef => tdef.FullName)
                    .Select(group => group.First()) // unique
                    .ToArray();

                // get target types (elemental job structs)
                TypeDefinition[] targetTypes = elementalTypes.Where(tdef => IsTargetType(tdef)).ToArray();
                numUniqueJobs = targetTypes.Length;

                // cache
                StringBuilder typeStringBuilder = new(1024);

                // resolve generic parameters of targets
                GenericTypeResolver resolver = new(targetAssemblies, elementalTypes, 128);
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
            }
            finally
            {
                // unload assemblies
                foreach (var a in targetAssemblies)
                    a?.Dispose();
            }

            GC.Collect();

            return resolvedTypeStrings.Distinct().ToArray();
        }


        private static bool IsTargetAssembly(Assembly assembly)
        {
            // dynamic assemblies are created by Emit, making them impossible to read from disk
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

            // must be burst compiled and not explicitly excluded
            HashSet<string> attributeTypes = type.CustomAttributes.Select(attr => attr.AttributeType.FullName).ToHashSet();
            return attributeTypes.Contains(BurstCompileAttribute_FullName)
                && !attributeTypes.Contains(DisableGenericBurstJobRegistryAttribute_FullName);
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
                TypeReference[] genericArgs = type.IsGenericInstance ? ((GenericInstanceType)type).GenericArguments.ToArray() : Array.Empty<TypeReference>();
                BuildResolvedTypeName(sb, type, genericArgs);
            }

            sb.Append('>');
        }
    }
}
