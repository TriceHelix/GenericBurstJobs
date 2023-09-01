using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine.Assertions;

namespace TriceHelix.GenericBurstJobs.Editor
{
    internal sealed class GenericTypeResolver
    {
        private static readonly string[] BuiltinTypeNames = new[]
        {
            // these are the names of types which will always be excluded from analysis
            "System.Void",
            "System.Object",
            "System.ValueType",
            "System.Boolean",
            "System.Byte",
            "System.SByte",
            "System.Char",
            "System.Single",
            "System.Double",
            "System.Int16",
            "System.UInt16",
            "System.Int32",
            "System.UInt32",
            "System.Int64",
            "System.UInt64",
            "System.Decimal"
        };

        private readonly ImmutableMultiDictionary<string, GenericInstanceType> AllGenericInstanceTypes;
        private readonly ImmutableMultiDictionary<string, GenericInstanceMethod> AllInvokedGenericInstanceMethods;
        private readonly List<TypeReference> ResolvedTypes;
        private readonly HashSet<CircLogID> CircularLogicPreventinator = new(1024); // TODO: recieve approval of Dan Povenmire
        private int genericParameterCount = 0;
        private List<TypeReference>[] typeLists = Array.Empty<List<TypeReference>>();
        private int[] outputPath = Array.Empty<int>();
        
        public int ResultCount => genericParameterCount > 0 ? (ResolvedTypes.Count / genericParameterCount) : 0;


        internal GenericTypeResolver(AssemblyDefinition[] targetAssemblies, TypeDefinition[] elementalTypes, int capacity = 1)
        {
            Assert.IsTrue(targetAssemblies != null);
            Assert.IsTrue(elementalTypes != null);
            Assert.IsTrue(capacity >= 0);

            ResolvedTypes = new List<TypeReference>(capacity);

            HashSet<string> targetAssemblySet = targetAssemblies.Select(a => a.Name.Name).ToHashSet();
            MethodDefinition[] elementalMethods = elementalTypes.Where(tdef => tdef.HasMethods).SelectMany(tdef => tdef.Methods).ToArray();
            MethodImplTree methodImplTree = new(targetAssemblySet, elementalMethods);

            // analyze types
            Dictionary<string, GenericInstanceType> gits = new(4096);
            Dictionary<string, GenericInstanceMethod> gims = new(4096);
            AnalyzeTypes(targetAssemblySet, methodImplTree, gits, gims, elementalTypes);

            // add everything to dictionaries
            AllGenericInstanceTypes = new ImmutableMultiDictionary<string, GenericInstanceType>(gits.Values.Select(git => new KeyValuePair<string, GenericInstanceType>(git.GetNormalizedName(), git)).ToArray());
            AllInvokedGenericInstanceMethods = new ImmutableMultiDictionary<string, GenericInstanceMethod>(gims.Values.Select(gim => new KeyValuePair<string, GenericInstanceMethod>(gim.GetNormalizedName(), gim)).ToArray());
        }


        internal void Resolve(TypeReference target)
        {
            Clear();

            if (target == null)
                throw new ArgumentNullException(nameof(target));

            if (!target.HasGenericParameters)
                throw new InvalidOperationException($"Cannot resolve type without generic parameters ({target.FullName})");

            genericParameterCount = target.GenericParameters.Count;
            typeLists = new List<TypeReference>[genericParameterCount];
            outputPath = new int[genericParameterCount];
            for (int i = 0; i < genericParameterCount; i++)
                typeLists[i] = new List<TypeReference>(2);

            string targetTypeName = target.GetNormalizedName();

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
                    CircularLogicPreventinator.Clear();

                    if (gp.Owner is MethodReference mref)
                    {
                        // unresolved type belongs to method
                        ResolveGenericSourceRecursive(mref.GetNormalizedName(), mref.GenericParameters.Count, false, gp.Position, resolvedGenerics);
                    }
                    else
                    {
                        // unresolved type belongs to another type
                        TypeReference unresolvedSource = GetGenericParameterSource(gp);
                        ResolveGenericSourceRecursive(unresolvedSource.GetNormalizedName(), unresolvedSource.GenericParameters.Count, true, gp.Position, resolvedGenerics);
                    }

                    if (resolvedGenerics.Count <= 0)
                        return; // unable to resolve generic parameter

                    typeLists[i] = resolvedGenerics.GroupBy(tref => tref.FullName).Select(group => group.First()).ToList();
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


        private void ResolveGenericSourceRecursive(string normalizedName, int genericArgCount, bool isType, int parameterIndex, List<TypeReference> results)
        {
            if (!CircularLogicPreventinator.Add(new CircLogID(normalizedName, genericArgCount, parameterIndex)))
                return;

            if (isType)
            {
                foreach (GenericInstanceType type in AllGenericInstanceTypes.GetValuesForKey(normalizedName))
                    AnalyzeGenericArgument(type.GenericArguments[parameterIndex]);
            }
            else
            {
                foreach (GenericInstanceMethod method in AllInvokedGenericInstanceMethods.GetValuesForKey(normalizedName))
                    AnalyzeGenericArgument(method.GenericArguments[parameterIndex]);
            }

            // assumes that given type reference has matching number of generic arguments
            void AnalyzeGenericArgument(TypeReference arg)
            {
                if (arg.IsGenericParameter && arg is GenericParameter gp)
                {
                    // argument is unresolved
                    if (gp.Owner is MethodReference mref)
                    {
                        // belongs to method
                        ResolveGenericSourceRecursive(mref.GetNormalizedName(), mref.GenericParameters.Count, false, gp.Position, results);
                    }
                    else
                    {
                        // belongs to type
                        TypeReference unresolvedSource = GetGenericParameterSource(gp);
                        ResolveGenericSourceRecursive(unresolvedSource.GetNormalizedName(), unresolvedSource.GenericParameters.Count, true, gp.Position, results);
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


        private static void AnalyzeTypes(
            HashSet<string> targetAssemblySet,
            MethodImplTree methodImplTree,
            Dictionary<string, GenericInstanceType> gits,
            Dictionary<string, GenericInstanceMethod> gims,
            TypeDefinition[] elementalTypes)
        {
            Assert.IsTrue(targetAssemblySet != null);
            Assert.IsTrue(gits != null);
            Assert.IsTrue(gims != null);
            Assert.IsTrue(elementalTypes != null);
            
            if (elementalTypes.Length <= 0)
                return;

            HashSet<string> analyzedTypes = new(BuiltinTypeNames.Length + (elementalTypes.Length * 16));
            Queue<GenericInstanceType> cachedQueue = new(128);

            // prevent analysis of built-in types
            foreach (string builtinType in BuiltinTypeNames)
                analyzedTypes.Add(builtinType);

            // analysis stack (much better than recursion)
            Stack<TypeReference> analysis = new(elementalTypes.Length * 4);
            for (int i = 0; i < elementalTypes.Length; i++)
                analysis.Push(elementalTypes[i]);

            while (analysis.TryPop(out TypeReference tref))
            {
                // unpack type
                while (tref.IsArray || tref.IsPointer || tref.IsByReference)
                    tref = tref.GetElementType();

                if (tref.IsGenericParameter)
                    continue;

                // no duplicate analysis
                string fullName = tref.FullName;
                if (string.IsNullOrEmpty(fullName) || !analyzedTypes.Add(fullName))
                    continue;

                bool isResolvable = tref.IsTargetType(targetAssemblySet);

                if (tref.IsGenericInstance)
                {
                    GenericInstanceType git = (GenericInstanceType)tref;
                    if (!isResolvable || gits.TryAdd(fullName, git))
                    {
                        // analyze generic arguments (this will happen even if type cannot be resolved)
                        foreach (GenericInstanceType garg in IterateGenericArgs(git, cachedQueue))
                            analysis.Push(garg);
                    }
                }

                if (!isResolvable)
                    continue;

                if (tref.TryResolve(out TypeDefinition tdef))
                {
                    // analyze base type
                    if (tdef.BaseType != null)
                        analysis.Push(tdef.BaseType);

                    // analyze declaring types
                    foreach (TypeReference declTref in IterateDeclarationChain(tref))
                        analysis.Push(declTref);

                    // analyze nested types
                    if (tdef.HasNestedTypes)
                    {
                        foreach (var nested in tdef.NestedTypes)
                            analysis.Push(nested);
                    }

                    // analyze field types
                    if (tdef.HasFields)
                    {
                        foreach (var field in tdef.Fields)
                            analysis.Push(field.FieldType);
                    }

                    if (tdef.HasMethods)
                    {
                        foreach (MethodDefinition mdef in tdef.Methods)
                            AnalyzeMethod(targetAssemblySet, methodImplTree, gims, analysis, mdef);
                    }
                }
            }
        }


        // this is a seperated part of AnalyzeTypes() for better readability and should only be used in that context
        private static void AnalyzeMethod(
            HashSet<string> targetAssemblySet,
            MethodImplTree methodImplTree,
            Dictionary<string, GenericInstanceMethod> gims,
            Stack<TypeReference> analysis,
            MethodDefinition method)
        {
            // analyze method return type
            analysis.Push(method.ReturnType);

            // analyze method parameter types
            if (method.HasParameters)
            {
                foreach (ParameterDefinition mp in method.Parameters)
                    analysis.Push(mp.ParameterType);
            }

            if (!method.HasBody)
                return;

            MethodBody body = method.Body;

            // analyze method variables
            if (body.HasVariables)
            {
                foreach (VariableDefinition mv in body.Variables)
                    analysis.Push(mv.VariableType);
            }

            // analyze method calls / constructors
            foreach (Instruction instr in body.Instructions)
            {
                Code code = instr.OpCode.Code;
                if (code is not (Code.Call or Code.Callvirt or Code.Newobj) || instr.Operand is not MethodReference call)
                    continue;

                // no external calls/constructors
                if (!call.IsTargetMethod(targetAssemblySet))
                    continue;

                // analyze declaring type
                analysis.Push(call.DeclaringType);

                if (code is not Code.Newobj) // next section is only for methods
                {
                    if (call.IsGenericInstance && call is GenericInstanceMethod gimCall)
                    {
                        // analyze generic arguments
                        if (gimCall.HasGenericArguments)
                        {
                            foreach (TypeReference garg in gimCall.GenericArguments)
                                analysis.Push(garg);
                        }

                        gims.TryAdd(gimCall.FullName, gimCall);

                        // add overrides
                        foreach (MethodDefinition ovrdMdef in methodImplTree.IterateOverridesAndImplementations(gimCall))
                        {
                            GenericInstanceMethod ovrdGim = new(ovrdMdef);
                            if (gimCall.HasGenericArguments)
                            {
                                // copy generic args
                                var destArgs = ovrdGim.GenericArguments;
                                foreach (TypeReference garg in gimCall.GenericArguments)
                                    destArgs.Add(garg);
                            }

                            gims.TryAdd(ovrdGim.FullName, ovrdGim);
                        }
                    }
                }
            }
        }


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
                    // unpack type
                    TypeReference realArg = arg;
                    while (realArg.IsArray || realArg.IsPointer || realArg.IsByReference)
                        realArg = realArg.GetElementType();

                    if (realArg.IsGenericInstance)
                        cachedQueue.Enqueue((GenericInstanceType)realArg);
                }
            }
        }


        private readonly struct CircLogID : IEquatable<CircLogID>
        {
            internal readonly string NormalizedName;
            internal readonly int GenericArgCount;
            internal readonly int ParameterIndex;


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal CircLogID(string normalizedName, int genericArgCount, int parameterIndex)
            {
                NormalizedName = normalizedName;
                GenericArgCount = genericArgCount;
                ParameterIndex = parameterIndex;
            }


            public readonly bool Equals(CircLogID other)
            {
                return GenericArgCount == other.GenericArgCount
                    && ParameterIndex == other.ParameterIndex
                    && NormalizedName == other.NormalizedName;
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override readonly int GetHashCode()
            {
                return HashCode.Combine(NormalizedName, GenericArgCount, ParameterIndex);
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override readonly bool Equals(object obj)
            {
                return obj is CircLogID value && Equals(value);
            }
        }
    }
}
