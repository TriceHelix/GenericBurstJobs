using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Assertions;

namespace TriceHelix.GenericBurstJobs.Editor
{
    internal class GenericResolver
    {
        private const string VOID_TYPE_FULLNAME = "System.Void";

        private readonly ImmutableMultiDictionary<string, GenericInstanceType> AllGenericInstanceTypes;
        private readonly ImmutableMultiDictionary<string, GenericInstanceMethod> AllInvokedGenericInstanceMethods;
        private readonly List<TypeReference> ResolvedTypes;
        private int genericParameterCount = 0;
        private List<TypeReference>[] typeLists = Array.Empty<List<TypeReference>>();
        private int[] outputPath = Array.Empty<int>();

        public int ResultCount => genericParameterCount > 0 ? (ResolvedTypes.Count / genericParameterCount) : 0;


        internal GenericResolver(TypeDefinition[] allTypes, int capacity = 1)
        {
            ResolvedTypes = new List<TypeReference>(capacity);

            FieldDefinition[] allFields = allTypes.Where(t => t.HasFields).SelectMany(t => t.Fields).ToArray();
            MethodDefinition[] allMethods = allTypes.Where(t => t.HasMethods).SelectMany(t => t.Methods).ToArray();

            // literal field types
            IEnumerable<GenericInstanceType> gits1 = allFields
                .Select(f =>
                {
                    TypeReference t = f.FieldType;
                    while (t.IsArray) t = t.GetElementType();
                    return t;
                })
                .Where(t => t.IsGenericInstance)
                .Select(t => t as GenericInstanceType);

            // declaring types of fields
            IEnumerable<GenericInstanceType> gits2 = allFields
                .SelectMany(f => IterateDeclarationChain(f))
                .Where(t => t.IsGenericInstance)
                .Select(t => t as GenericInstanceType);

            // method return types, parameters, variables, and declaring types
            IEnumerable<GenericInstanceType> gits3 = allMethods
                .SelectMany(m =>
                {
                    IEnumerable<TypeReference> types = Array.Empty<TypeReference>();

                    // return type
                    if (m.ReturnType.FullName != VOID_TYPE_FULLNAME) // exclude void
                        types = types.Append(m.ReturnType);

                    // parameter types
                    if (m.HasParameters)
                        types = types.Concat(m.Parameters.Select(p => p.ParameterType));

                    // variable types
                    if (m.HasBody)
                        types = types.Concat(m.Body.Variables.Select(v => v.VariableType));

                    // declaring types
                    types = types.Concat(IterateDeclarationChain(m));

                    return types;
                })
                .Select(t =>
                {
                    while (t.IsArray) t = t.GetElementType();
                    return t;
                })
                .Where(t => t.IsGenericInstance)
                .Select(t => t as GenericInstanceType);

            // invoked generic methods
            GenericInstanceMethod[] gims = allMethods
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Where(i => i.OpCode == OpCodes.Call && i.Operand != null)
                .Select(i => i.Operand as MethodReference)
                .Where(m => m.IsGenericInstance)
                .Select(m => m as GenericInstanceMethod)
                .GroupBy(m => m.FullName)
                .Select(g => g.First()) // unique
                .ToArray();

            // generic arguments of invoked generic methods
            IEnumerable<GenericInstanceType> gits4 = gims
                .Where(m => m.HasGenericArguments)
                .SelectMany(m => m.GenericArguments)
                .Where(t => t.IsGenericInstance)
                .Select(t => t as GenericInstanceType);

            // declaring types of invoked generic methods
            IEnumerable<GenericInstanceType> gits5 = gims
                .SelectMany(m => IterateDeclarationChain(m))
                .Where(m => m.IsGenericInstance)
                .Select(m => m as GenericInstanceType);

            // merge generic instance types and add nested generic args
            Queue<GenericInstanceType> cachedQueue = new(128);
            IEnumerable<GenericInstanceType> gits = gits1.Concat(gits2).Concat(gits3).Concat(gits4).Concat(gits5)
                .GroupBy(t => t.FullName)
                .Select(g => g.First()) // unique
                .SelectMany(t => IterateGenericArgs(t, cachedQueue))
                .GroupBy(t => t.FullName)
                .Select(g => g.First()); // unique

            // add everything to dictionaries
            AllGenericInstanceTypes = new ImmutableMultiDictionary<string, GenericInstanceType>(
                gits.Select(t => new KeyValuePair<string, GenericInstanceType>(NormalizeTypeName(t.FullName), t))
                    .ToArray()
                );

            AllInvokedGenericInstanceMethods = new ImmutableMultiDictionary<string, GenericInstanceMethod>(
                gims.Select(m => new KeyValuePair<string, GenericInstanceMethod>(NormalizeMethodName(m.FullName), m))
                    .ToArray()
                );
        }


        internal void Resolve(TypeReference target)
        {
            Clear();

            if (target == null)
                throw new ArgumentNullException(nameof(target));

            if (!target.HasGenericParameters)
                throw new ArgumentException(nameof(target));

            genericParameterCount = target.GenericParameters.Count;
            typeLists = new List<TypeReference>[genericParameterCount];
            outputPath = new int[genericParameterCount];
            for (int i = 0; i < genericParameterCount; i++)
                typeLists[i] = new List<TypeReference>(2);

            string targetTypeName = NormalizeTypeName(target.FullName);

            // resolve all occurrences of type
            foreach (GenericInstanceType varType in AllGenericInstanceTypes.GetValuesForKey(targetTypeName))
            {
                if (varType.GenericArguments.Count == genericParameterCount)
                    ResolveField(varType);
            }
        }


        internal void Clear()
        {
            genericParameterCount = 0;
            ResolvedTypes.Clear();
            typeLists = Array.Empty<List<TypeReference>>();
            outputPath = Array.Empty<int>();
        }


        internal TypeReference[] GetResult(int index)
        {
            TypeReference[] result = new TypeReference[genericParameterCount];
            GetResultNoAlloc(index, result);
            return result;
        }


        internal void GetResultNoAlloc(int index, TypeReference[] result)
        {
            int offset = genericParameterCount * index;
            if (offset < 0 || offset + genericParameterCount > ResolvedTypes.Count)
                throw new IndexOutOfRangeException($"Invalid Result Index ({index})");

            for (int i = 0; i < genericParameterCount; i++)
                result[i] = ResolvedTypes[offset + i];
        }


        private void ResolveField(GenericInstanceType type)
        {
            for (int i = 0; i < genericParameterCount; i++)
            {
                List<TypeReference> resolvedGenerics = typeLists[i];
                resolvedGenerics.Clear();

                TypeReference at = type.GenericArguments[i];
                if (at.IsGenericParameter && at is GenericParameter gp)
                {
                    if (gp.Owner is MethodReference mref)
                    {
                        // unresolved type belongs to method
                        ResolveGenericSourceRecursive(mref.FullName, mref.GenericParameters.Count, false, gp.Position, resolvedGenerics);
                    }
                    else
                    {
                        // unresolved type belongs to another type
                        TypeReference unresolvedSource = GetGenericParameterSource(gp);
                        ResolveGenericSourceRecursive(unresolvedSource.FullName, unresolvedSource.GenericParameters.Count, true, gp.Position, resolvedGenerics);
                    }

                    if (resolvedGenerics.Count <= 0)
                        return; // unable to resolve generic type

                    resolvedGenerics = resolvedGenerics.GroupBy(t => t.FullName).Select(g => g.First()).ToList();
                }
                else
                {
                    // found a single resolved type
                    resolvedGenerics.Add(at);
                }
            }

            int pointer = genericParameterCount - 1;
            for (int i = 0; i < genericParameterCount; i++)
                outputPath[i] = typeLists[i].Count - 1;

            // add resolved type combinations to results
            while (pointer >= 0)
            {
                for (int i = 0; i < genericParameterCount; i++)
                    ResolvedTypes.Add(typeLists[i][outputPath[i]]);

                for (int j = genericParameterCount - 1; j >= 0; j--)
                {
                    if (--outputPath[j] >= 0)
                        break;

                    outputPath[j] = typeLists[j].Count - 1; // roll over

                    if (j == pointer)
                    {
                        pointer--;
                        break;
                    }
                }
            }
        }


        private void ResolveGenericSourceRecursive(string targetName, int genericArgCount, bool isType, int parameterIndex, List<TypeReference> results)
        {
            if (isType)
            {
                foreach (GenericInstanceType varType in AllGenericInstanceTypes.GetValuesForKey(NormalizeTypeName(targetName)))
                {
                    if (genericArgCount == varType.GenericArguments.Count)
                        AnalyzeGenericArgument(varType.GenericArguments[parameterIndex]);
                }
            }
            else
            {
                foreach (GenericInstanceMethod method in AllInvokedGenericInstanceMethods.GetValuesForKey(NormalizeMethodName(targetName)))
                {
                    if (genericArgCount == method.GenericArguments.Count)
                        AnalyzeGenericArgument(method.GenericArguments[parameterIndex]);
                }
            }

            void AnalyzeGenericArgument(TypeReference arg)
            {
                if (arg.IsGenericParameter && arg is GenericParameter gp)
                {
                    // argument is unresolved
                    if (gp.Owner is MethodReference mref)
                    {
                        // belongs to method
                        ResolveGenericSourceRecursive(mref.FullName, mref.GenericParameters.Count, false, gp.Position, results);
                    }
                    else
                    {
                        // belongs to type
                        TypeReference unresolvedSource = GetGenericParameterSource(gp);
                        ResolveGenericSourceRecursive(unresolvedSource.FullName, unresolvedSource.GenericParameters.Count, true, gp.Position, results);
                    }
                }
                else
                {
                    // found a resolved type
                    results.Add(arg);
                }
            }
        }


        private TypeReference GetGenericParameterSource(GenericParameter genericParam)
        {
            Assert.IsTrue(genericParam != null);

            int pos = genericParam.Position;
            TypeReference dt1 = genericParam.DeclaringType;
            TypeReference result = dt1;

            // traverse nested type hierarchy (up) and find deepest type with matching generic parameter
            while (dt1 != null)
            {
                TypeReference dt2 = dt1.DeclaringType;
                if (dt2 == null || !dt2.HasGenericParameters)
                    break;

                if (pos >= dt2.GenericParameters.Count)
                {
                    // too far up in hierarchy
                    break;
                }
                else if (dt2.GenericParameters.Count < dt1.GenericParameters.Count)
                {
                    // only change result when candidate with less generic parameters is found
                    result = dt2;
                }

                dt1 = dt2;
            }

            return result;
        }


        internal static string NormalizeTypeName(string typeString)
        {
            if (string.IsNullOrEmpty(typeString))
                return typeString;

            int idx = SkipTypeString(typeString);
            return idx >= 0 ? typeString[..idx] : typeString;
        }


        internal static string NormalizeMethodName(string methodString)
        {
            if (string.IsNullOrEmpty(methodString))
                return methodString;

            int idx0 = Math.Max(0, methodString.IndexOf(' '));              // skip searching in return type
            int idx1 = methodString.IndexOf("::", idx0);                    // find method name
            int idx2 = methodString.IndexOf('<', idx0, idx1 - idx0);        // generic parent type may be explicitly resolved before method name
            int idx3 = methodString.IndexOf('<', idx1);                     // skip generic method params
            int idx4 = methodString.IndexOf('(', Math.Max(idx1, idx3));     // include rest of method signature

            if (idx2 < 0) idx2 = idx1;
            if (idx3 < 0) idx3 = idx4;

            return (idx1 >= 0 && idx2 >= 0 && idx3 >= 0 && idx4 >= 0)
                ? (methodString[..idx2] + methodString[idx1..idx3] + methodString[idx4..])
                : methodString;
        }


        private static int SkipTypeString(string s, int startIndex, int count)
        {
            int result = startIndex;

            bool hitAnyToken = false;
            bool hitNonGenericSubclassFlag = false;
            int lim = startIndex + count;
            for (int i = startIndex; i < lim; i++)
            {
                switch (s[i])
                {
                    case '/':
                        hitAnyToken = true;
                        hitNonGenericSubclassFlag = true;
                        break;

                    case '`':
                        hitAnyToken = true;
                        hitNonGenericSubclassFlag = false;
                        result = i;
                        break;

                    case '<':
                        if (hitNonGenericSubclassFlag) result = i;
                        goto LOOP_BREAK;
                }
            }

            if (!hitAnyToken || hitNonGenericSubclassFlag)
                result = startIndex + count;


            LOOP_BREAK:
            return result;
        }
        internal static int SkipTypeString(string s, int startIndex) => SkipTypeString(s, startIndex, s.Length - startIndex);
        internal static int SkipTypeString(string s) => SkipTypeString(s, 0, s.Length);


        private static IEnumerable<TypeReference> IterateDeclarationChain(MemberReference member)
        {
            Assert.IsTrue(member != null);

            TypeReference t = member.DeclaringType;
            while (t != null)
            {
                yield return t;
                t = t.DeclaringType;
            }
        }


        // includes given type in return iterator
        private static IEnumerable<GenericInstanceType> IterateGenericArgs(GenericInstanceType type, Queue<GenericInstanceType> cachedQueue)
        {
            Assert.IsTrue(type != null);

            cachedQueue.Clear();
            cachedQueue.Enqueue(type);

            while (cachedQueue.TryDequeue(out var t))
            {
                yield return t;

                if (!t.HasGenericArguments)
                    continue;

                foreach (var arg in t.GenericArguments)
                {
                    if (arg.IsGenericInstance)
                        cachedQueue.Enqueue(arg as GenericInstanceType);
                }
            }
        }
    }
}
