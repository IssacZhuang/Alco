using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Alco.Profiler.Weaver;

/// <summary>
/// Rewrites selected managed methods with exception-safe profiler hooks.
/// </summary>
public sealed class MethodProfilerWeaver
{
    private const string WeaverVersion = "1";

    private sealed class InstrumentationPlan
    {
        public required MethodDefinition BodyMethod { get; init; }
        public required MethodDefinition SourceMethod { get; init; }
        public required MethodProfileDescriptor Descriptor { get; init; }
        public required ulong MethodId { get; init; }
        public required MethodProfileTag Tags { get; init; }
        public required MethodProfileContextCapture ContextCapture { get; init; }
        public required string? RequiredRuntimeInterface { get; init; }
        public required string RuleName { get; init; }
    }

    private sealed class EffectiveDecision
    {
        public MethodProfileDecision Decision;
        public MethodProfileTag Tags;
        public MethodProfileContextCapture ContextCapture;
        public string? RequiredRuntimeInterface;
        public string RuleName = "none";
    }

    private readonly IReadOnlyList<IMethodProfileRule> _rules;

    /// <summary>
    /// Initializes a Weaver with rules in deterministic evaluation order.
    /// </summary>
    /// <param name="rules">Engine rules followed by game rules.</param>
    public MethodProfilerWeaver(IReadOnlyList<IMethodProfileRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }

    /// <summary>
    /// Rewrites an assembly and writes a deterministic text report.
    /// </summary>
    /// <param name="assemblyPath">Intermediate assembly path.</param>
    /// <param name="pdbPath">Optional Portable PDB path.</param>
    /// <param name="reportPath">Report output path.</param>
    /// <param name="searchDirectories">Additional directories used to resolve referenced metadata.</param>
    public void Weave(
        string assemblyPath,
        string? pdbPath,
        string reportPath,
        IReadOnlyList<string>? searchDirectories = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        string fullAssemblyPath = Path.GetFullPath(assemblyPath);
        byte[] lockIdentity = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(fullAssemblyPath.ToUpperInvariant()));
        string mutexName = "AlcoMethodProfilerWeaver_" + Convert.ToHexString(lockIdentity);
        using var mutex = new Mutex(false, mutexName);
        bool lockTaken = false;
        try
        {
            try
            {
                lockTaken = mutex.WaitOne(TimeSpan.FromMinutes(5));
            }
            catch (AbandonedMutexException)
            {
                lockTaken = true;
            }
            if (!lockTaken)
            {
                throw new TimeoutException($"Timed out waiting to weave {fullAssemblyPath}.");
            }

            WeaveCore(fullAssemblyPath, pdbPath, reportPath, searchDirectories);
        }
        finally
        {
            if (lockTaken)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private void WeaveCore(
        string assemblyPath,
        string? pdbPath,
        string reportPath,
        IReadOnlyList<string>? searchDirectories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);

        string fullAssemblyPath = Path.GetFullPath(assemblyPath);
        string? fullPdbPath = string.IsNullOrWhiteSpace(pdbPath) ? null : Path.GetFullPath(pdbPath);
        string backupAssemblyPath = Path.Combine(
            Path.GetDirectoryName(fullAssemblyPath)!,
            Path.GetFileNameWithoutExtension(fullAssemblyPath) + ".methodprofiler.original.dll");
        string? backupPdbPath = fullPdbPath == null
            ? null
            : Path.ChangeExtension(backupAssemblyPath, ".pdb");
        PreparePristineBackup(fullAssemblyPath, fullPdbPath, backupAssemblyPath, backupPdbPath);

        string tempAssemblyPath = fullAssemblyPath + ".methodprofiler.tmp";
        string? tempPdbPath = fullPdbPath == null ? null : Path.ChangeExtension(tempAssemblyPath, ".pdb");
        DeleteIfExists(tempAssemblyPath);
        DeleteIfExists(tempPdbPath);

        bool readSymbols = backupPdbPath != null && File.Exists(backupPdbPath);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(backupAssemblyPath)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(ProfilerHooks).Assembly.Location)!);
        if (searchDirectories != null)
        {
            for (int i = 0; i < searchDirectories.Count; i++)
            {
                string directory = Path.GetFullPath(searchDirectories[i]);
                if (Directory.Exists(directory))
                {
                    resolver.AddSearchDirectory(directory);
                }
            }
        }
        var readerParameters = new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadSymbols = readSymbols,
            SymbolReaderProvider = readSymbols ? new PortablePdbReaderProvider() : null,
            InMemory = true,
        };

        var report = new List<string>();
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(backupAssemblyPath, readerParameters))
        {
            List<InstrumentationPlan> plans = BuildPlans(assembly.MainModule, report);
            if (plans.Count == 0)
            {
                RestorePristine(fullAssemblyPath, fullPdbPath, backupAssemblyPath, backupPdbPath);
                WriteReport(reportPath, assembly.Name.Name, report, 0);
                return;
            }

            MethodReference enter = assembly.MainModule.ImportReference(
                typeof(ProfilerHooks).GetMethod(nameof(ProfilerHooks.Enter))!);
            MethodReference exit = assembly.MainModule.ImportReference(
                typeof(ProfilerHooks).GetMethod(nameof(ProfilerHooks.Exit))!);
            for (int i = 0; i < plans.Count; i++)
            {
                Instrument(assembly.MainModule, plans[i], enter, exit);
            }

            InjectRegistrations(assembly.MainModule, plans);
            System.Reflection.ConstructorInfo markerConstructor = typeof(MethodProfilerWovenAttribute)
                .GetConstructor([typeof(string)])!;
            var marker = new CustomAttribute(assembly.MainModule.ImportReference(markerConstructor));
            marker.ConstructorArguments.Add(new CustomAttributeArgument(
                assembly.MainModule.TypeSystem.String,
                WeaverVersion));
            assembly.CustomAttributes.Add(marker);

            var writerParameters = new WriterParameters
            {
                WriteSymbols = readSymbols,
                SymbolWriterProvider = readSymbols ? new PortablePdbWriterProvider() : null,
            };
            assembly.Write(tempAssemblyPath, writerParameters);
        }

        using (AssemblyDefinition validation = AssemblyDefinition.ReadAssembly(tempAssemblyPath))
        {
            if (!HasWovenMarker(validation))
            {
                throw new InvalidOperationException("Post-write validation did not find the profiler marker.");
            }
        }

        try
        {
            File.Move(tempAssemblyPath, fullAssemblyPath, true);
            if (readSymbols && fullPdbPath != null && tempPdbPath != null)
            {
                File.Move(tempPdbPath, fullPdbPath, true);
            }
        }
        catch
        {
            RestorePristine(fullAssemblyPath, fullPdbPath, backupAssemblyPath, backupPdbPath);
            throw;
        }
        finally
        {
            DeleteIfExists(tempAssemblyPath);
            DeleteIfExists(tempPdbPath);
        }
        WriteReport(reportPath, Path.GetFileNameWithoutExtension(fullAssemblyPath), report,
            report.Count(line => line.StartsWith("INCLUDE ", StringComparison.Ordinal)));
    }

    private List<InstrumentationPlan> BuildPlans(ModuleDefinition module, List<string> report)
    {
        var plans = new List<InstrumentationPlan>();
        var stateMachineBodies = new HashSet<MethodDefinition>();
        IReadOnlyDictionary<MethodDefinition, IReadOnlyList<string>> interfaceSlotIndex =
            BuildEffectiveInterfaceSlotIndex(module);
        List<MethodDefinition> methods = GetAllTypes(module)
            .SelectMany(static type => type.Methods)
            .OrderBy(static method => method.DeclaringType.FullName, StringComparer.Ordinal)
            .ThenBy(static method => method.FullName, StringComparer.Ordinal)
            .ToList();

        for (int i = 0; i < methods.Count; i++)
        {
            MethodDefinition sourceMethod = methods[i];
            MethodDefinition bodyMethod = ResolveStateMachineBody(sourceMethod) ?? sourceMethod;
            if (bodyMethod != sourceMethod)
            {
                stateMachineBodies.Add(bodyMethod);
            }
            else if (stateMachineBodies.Contains(sourceMethod) || IsGeneratedInfrastructure(sourceMethod))
            {
                continue;
            }

            MethodProfileDescriptor descriptor = CreateDescriptor(
                sourceMethod,
                bodyMethod,
                module,
                interfaceSlotIndex);
            EffectiveDecision decision = Evaluate(descriptor);
            if (decision.Decision != MethodProfileDecision.Include)
            {
                report.Add($"SKIP {descriptor.DeclaringTypeName}::{descriptor.Signature} [{decision.RuleName}]");
                continue;
            }
            if (!descriptor.IsSupported)
            {
                throw new InvalidOperationException(
                    $"Rule {decision.RuleName} explicitly included unsupported method " +
                    $"{descriptor.DeclaringTypeName}::{descriptor.Signature}: {descriptor.UnsupportedReason}");
            }
            if (decision.ContextCapture == MethodProfileContextCapture.RuntimeType &&
                (bodyMethod.IsStatic || bodyMethod.DeclaringType.IsValueType || bodyMethod != sourceMethod))
            {
                throw new InvalidOperationException(
                    $"Rule {decision.RuleName} requested runtime context for unsupported method " +
                    $"{descriptor.DeclaringTypeName}::{descriptor.Signature}.");
            }

            ulong methodId = ComputeMethodId(module.Assembly.Name.Name + "|" +
                descriptor.DeclaringTypeName + "|" + descriptor.Signature);
            plans.Add(new InstrumentationPlan
            {
                BodyMethod = bodyMethod,
                SourceMethod = sourceMethod,
                Descriptor = descriptor,
                MethodId = methodId,
                Tags = decision.Tags,
                ContextCapture = decision.ContextCapture,
                RequiredRuntimeInterface = decision.RequiredRuntimeInterface,
                RuleName = decision.RuleName,
            });
            report.Add($"INCLUDE {descriptor.DeclaringTypeName}::{descriptor.Signature} " +
                $"[{decision.RuleName}] id={methodId}");
        }

        var duplicate = plans.GroupBy(static plan => plan.MethodId).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate != null)
        {
            throw new InvalidOperationException($"Method profiler ID collision in {module.Assembly.Name.Name}: {duplicate.Key}.");
        }
        return plans;
    }

    private EffectiveDecision Evaluate(MethodProfileDescriptor descriptor)
    {
        var result = new EffectiveDecision();
        for (int i = 0; i < _rules.Count; i++)
        {
            IMethodProfileRule rule = _rules[i];
            MethodProfileRuleResult change = rule.Evaluate(descriptor);
            if (change.Decision == MethodProfileDecision.Inherit)
            {
                continue;
            }

            result.Decision = change.Decision;
            result.RuleName = rule.Name;
            if (change.Decision == MethodProfileDecision.Exclude)
            {
                result.Tags = MethodProfileTag.None;
                result.ContextCapture = MethodProfileContextCapture.None;
                result.RequiredRuntimeInterface = null;
                continue;
            }

            result.Tags |= change.Tags;
            result.ContextCapture = change.ContextCapture;
            result.RequiredRuntimeInterface = change.RequiredRuntimeInterface;
        }
        return result;
    }

    private static MethodProfileDescriptor CreateDescriptor(
        MethodDefinition sourceMethod,
        MethodDefinition bodyMethod,
        ModuleDefinition module,
        IReadOnlyDictionary<MethodDefinition, IReadOnlyList<string>> interfaceSlotIndex)
    {
        string? unsupportedReason = GetUnsupportedReason(bodyMethod);
        return new MethodProfileDescriptor
        {
            AssemblyName = module.Assembly.Name.Name,
            Namespace = sourceMethod.DeclaringType.Namespace,
            DeclaringTypeName = sourceMethod.DeclaringType.FullName.Replace('/', '+'),
            MethodName = sourceMethod.Name,
            Signature = NormalizeSignature(sourceMethod),
            TypeHierarchy = GetTypeHierarchy(sourceMethod.DeclaringType),
            EffectiveInterfaceSlots = interfaceSlotIndex.TryGetValue(sourceMethod, out IReadOnlyList<string>? slots)
                ? slots
                : Array.Empty<string>(),
            HasBody = bodyMethod.HasBody,
            IsStatic = sourceMethod.IsStatic,
            IsConstructor = sourceMethod.IsConstructor,
            IsAccessor = sourceMethod.IsGetter || sourceMethod.IsSetter || sourceMethod.IsAddOn || sourceMethod.IsRemoveOn,
            IsCompilerGenerated = HasCompilerGeneratedAttribute(sourceMethod) ||
                HasCompilerGeneratedAttribute(sourceMethod.DeclaringType),
            IsSupported = unsupportedReason == null,
            UnsupportedReason = unsupportedReason,
        };
    }

    private static string? GetUnsupportedReason(MethodDefinition method)
    {
        if (!method.HasBody)
        {
            return "method has no IL body";
        }
        if (method.IsPInvokeImpl || method.IsRuntime || method.IsInternalCall)
        {
            return "method implementation is external or runtime-provided";
        }
        if (method.CallingConvention == MethodCallingConvention.VarArg)
        {
            return "vararg methods are not supported";
        }
        if (method.ReturnType.IsByReference)
        {
            return "by-ref returns are not supported";
        }
        if (method.Body.Instructions.Any(static instruction =>
                instruction.OpCode == OpCodes.Jmp || instruction.OpCode == OpCodes.Tail))
        {
            return "jmp and tail-prefixed control flow are not supported";
        }
        if (method.IsConstructor && !method.IsStatic && FindConstructorEntry(method) == null)
        {
            return "constructor chain call was not found";
        }
        return null;
    }

    private static void Instrument(
        ModuleDefinition module,
        InstrumentationPlan plan,
        MethodReference enter,
        MethodReference exit)
    {
        MethodDefinition method = plan.BodyMethod;
        MethodBody body = method.Body;
        body.SimplifyMacros();
        body.InitLocals = true;
        var tokenVariable = new VariableDefinition(module.ImportReference(typeof(MethodProfileToken)));
        body.Variables.Add(tokenVariable);

        VariableDefinition? returnVariable = null;
        if (method.ReturnType.MetadataType != MetadataType.Void)
        {
            returnVariable = new VariableDefinition(module.ImportReference(method.ReturnType));
            body.Variables.Add(returnVariable);
        }

        Instruction entryTarget = method.IsConstructor && !method.IsStatic
            ? FindConstructorEntry(method)!.Next
            : body.Instructions[0];
        ILProcessor il = body.GetILProcessor();
        Instruction tryStart = Instruction.Create(OpCodes.Nop);
        var entryInstructions = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldc_I8, unchecked((long)plan.MethodId)),
            Instruction.Create(OpCodes.Ldc_I4, (int)plan.Tags),
        };
        if (plan.ContextCapture == MethodProfileContextCapture.RuntimeType)
        {
            entryInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        }
        else
        {
            entryInstructions.Add(Instruction.Create(OpCodes.Ldnull));
        }
        entryInstructions.Add(Instruction.Create(OpCodes.Call, enter));
        entryInstructions.Add(Instruction.Create(OpCodes.Stloc, tokenVariable));
        entryInstructions.Add(tryStart);
        for (int i = 0; i < entryInstructions.Count; i++)
        {
            il.InsertBefore(entryTarget, entryInstructions[i]);
        }

        Instruction finallyStart = Instruction.Create(OpCodes.Ldloc, tokenVariable);
        Instruction epilogue = returnVariable == null
            ? Instruction.Create(OpCodes.Ret)
            : Instruction.Create(OpCodes.Ldloc, returnVariable);
        Instruction finalReturn = returnVariable == null ? epilogue : Instruction.Create(OpCodes.Ret);

        Instruction[] originalReturns = body.Instructions.Where(static instruction => instruction.OpCode == OpCodes.Ret).ToArray();
        for (int i = 0; i < originalReturns.Length; i++)
        {
            Instruction instruction = originalReturns[i];
            if (returnVariable != null)
            {
                Instruction storeReturn = Instruction.Create(OpCodes.Stloc, returnVariable);
                il.InsertBefore(instruction, storeReturn);
                RetargetBranches(body, instruction, storeReturn);
            }
            instruction.OpCode = OpCodes.Leave;
            instruction.Operand = epilogue;
        }

        il.Append(finallyStart);
        il.Append(Instruction.Create(OpCodes.Call, exit));
        il.Append(Instruction.Create(OpCodes.Endfinally));
        il.Append(epilogue);
        if (returnVariable != null)
        {
            il.Append(finalReturn);
        }

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = tryStart,
            TryEnd = finallyStart,
            HandlerStart = finallyStart,
            HandlerEnd = epilogue,
        });
        body.OptimizeMacros();
    }

    private static void RetargetBranches(MethodBody body, Instruction oldTarget, Instruction newTarget)
    {
        for (int i = 0; i < body.Instructions.Count; i++)
        {
            Instruction instruction = body.Instructions[i];
            if (instruction.Operand == oldTarget)
            {
                instruction.Operand = newTarget;
                continue;
            }
            if (instruction.Operand is not Instruction[] targets)
            {
                continue;
            }
            for (int j = 0; j < targets.Length; j++)
            {
                if (targets[j] == oldTarget)
                {
                    targets[j] = newTarget;
                }
            }
        }
    }

    private static void InjectRegistrations(ModuleDefinition module, IReadOnlyList<InstrumentationPlan> plans)
    {
        TypeDefinition moduleType = module.Types[0];
        MethodDefinition? initializer = moduleType.Methods.FirstOrDefault(static method => method.Name == ".cctor");
        if (initializer == null)
        {
            initializer = new MethodDefinition(
                ".cctor",
                MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.SpecialName |
                MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            initializer.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            moduleType.Methods.Add(initializer);
        }

        MethodReference register = module.ImportReference(
            typeof(ProfilerHooks).GetMethod(nameof(ProfilerHooks.RegisterMethod))!);
        ILProcessor il = initializer.Body.GetILProcessor();
        Instruction target = initializer.Body.Instructions[0];
        foreach (InstrumentationPlan plan in plans.OrderBy(static plan => plan.MethodId))
        {
            il.InsertBefore(target, Instruction.Create(OpCodes.Ldc_I8, unchecked((long)plan.MethodId)));
            il.InsertBefore(target, Instruction.Create(OpCodes.Ldstr, module.Assembly.Name.Name));
            il.InsertBefore(target, Instruction.Create(OpCodes.Ldstr, plan.Descriptor.DeclaringTypeName));
            il.InsertBefore(target, Instruction.Create(OpCodes.Ldstr, plan.Descriptor.MethodName));
            il.InsertBefore(target, Instruction.Create(OpCodes.Ldstr, plan.Descriptor.Signature));
            il.InsertBefore(target, Instruction.Create(OpCodes.Ldc_I4, (int)plan.Tags));
            il.InsertBefore(target, plan.RequiredRuntimeInterface == null
                ? Instruction.Create(OpCodes.Ldnull)
                : Instruction.Create(OpCodes.Ldstr, plan.RequiredRuntimeInterface));
            il.InsertBefore(target, Instruction.Create(OpCodes.Call, register));
        }
        initializer.Body.OptimizeMacros();
    }

    private static MethodDefinition? ResolveStateMachineBody(MethodDefinition method)
    {
        CustomAttribute? attribute = method.CustomAttributes.FirstOrDefault(static attribute =>
            attribute.AttributeType.FullName == typeof(AsyncStateMachineAttribute).FullName ||
            attribute.AttributeType.FullName == typeof(IteratorStateMachineAttribute).FullName ||
            attribute.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute");
        if (attribute?.ConstructorArguments.Count != 1 || attribute.ConstructorArguments[0].Value is not TypeReference type)
        {
            return null;
        }

        TypeDefinition? stateMachine = SafeResolve(type);
        return stateMachine?.Methods.FirstOrDefault(static candidate =>
            candidate.Name == nameof(IAsyncStateMachine.MoveNext) && candidate.Parameters.Count == 0);
    }

    private static Instruction? FindConstructorEntry(MethodDefinition method)
    {
        return method.Body.Instructions.FirstOrDefault(static instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference called &&
            called.Name == ".ctor");
    }

    private static IReadOnlyList<string> GetTypeHierarchy(TypeDefinition type)
    {
        var result = new List<string>();
        TypeDefinition? current = type;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current != null && visited.Add(current.FullName))
        {
            result.Add(current.FullName.Replace('/', '+'));
            current = current.BaseType == null ? null : SafeResolve(current.BaseType);
        }
        return result;
    }

    private static IReadOnlyDictionary<MethodDefinition, IReadOnlyList<string>> BuildEffectiveInterfaceSlotIndex(
        ModuleDefinition module)
    {
        var mutable = new Dictionary<MethodDefinition, HashSet<string>>();
        foreach (TypeDefinition concreteType in GetAllTypes(module))
        {
            foreach (TypeDefinition interfaceType in GetAllInterfaces(concreteType))
            {
                foreach (MethodDefinition interfaceMethod in interfaceType.Methods)
                {
                    MethodDefinition? implementation = ResolveInterfaceImplementation(concreteType, interfaceMethod);
                    if (implementation == null)
                    {
                        continue;
                    }

                    if (!mutable.TryGetValue(implementation, out HashSet<string>? slots))
                    {
                        slots = new HashSet<string>(StringComparer.Ordinal);
                        mutable.Add(implementation, slots);
                    }
                    slots.Add(interfaceMethod.FullName);
                }
            }
        }

        return mutable.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value.Order(StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<TypeDefinition> GetAllInterfaces(TypeDefinition type)
    {
        var result = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
        TypeDefinition? current = type;
        var visitedTypes = new HashSet<string>(StringComparer.Ordinal);
        while (current != null && visitedTypes.Add(current.FullName))
        {
            for (int i = 0; i < current.Interfaces.Count; i++)
            {
                AddInterface(current.Interfaces[i].InterfaceType, result);
            }
            current = current.BaseType == null ? null : SafeResolve(current.BaseType);
        }
        return result.Values;
    }

    private static void AddInterface(TypeReference interfaceReference, Dictionary<string, TypeDefinition> result)
    {
        TypeDefinition? interfaceType = SafeResolve(interfaceReference);
        if (interfaceType == null || !result.TryAdd(interfaceType.FullName, interfaceType))
        {
            return;
        }
        for (int i = 0; i < interfaceType.Interfaces.Count; i++)
        {
            AddInterface(interfaceType.Interfaces[i].InterfaceType, result);
        }
    }

    private static MethodDefinition? ResolveInterfaceImplementation(
        TypeDefinition concreteType,
        MethodDefinition interfaceMethod)
    {
        TypeDefinition? current = concreteType;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current != null && visited.Add(current.FullName))
        {
            for (int i = 0; i < current.Methods.Count; i++)
            {
                MethodDefinition candidate = current.Methods[i];
                for (int j = 0; j < candidate.Overrides.Count; j++)
                {
                    if (candidate.Overrides[j].FullName == interfaceMethod.FullName)
                    {
                        return candidate;
                    }
                }
            }
            current = current.BaseType == null ? null : SafeResolve(current.BaseType);
        }

        current = concreteType;
        visited.Clear();
        while (current != null && visited.Add(current.FullName))
        {
            for (int i = 0; i < current.Methods.Count; i++)
            {
                MethodDefinition candidate = current.Methods[i];
                if (!candidate.IsStatic && candidate.IsPublic && MethodShapeMatches(candidate, interfaceMethod))
                {
                    return candidate;
                }
            }
            current = current.BaseType == null ? null : SafeResolve(current.BaseType);
        }
        return null;
    }

    private static bool MethodShapeMatches(MethodDefinition method, MethodDefinition candidate)
    {
        string methodName = method.Name.Contains('.') ? method.Name[(method.Name.LastIndexOf('.') + 1)..] : method.Name;
        if (!string.Equals(methodName, candidate.Name, StringComparison.Ordinal) ||
            method.Parameters.Count != candidate.Parameters.Count ||
            method.ReturnType.FullName != candidate.ReturnType.FullName)
        {
            return false;
        }
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            if (method.Parameters[i].ParameterType.FullName != candidate.Parameters[i].ParameterType.FullName)
            {
                return false;
            }
        }
        return true;
    }

    private static string NormalizeSignature(MethodDefinition method)
    {
        string parameters = string.Join(",", method.Parameters.Select(static parameter => parameter.ParameterType.FullName));
        return $"{method.ReturnType.FullName} {method.Name}({parameters})";
    }

    private static bool HasCompilerGeneratedAttribute(ICustomAttributeProvider provider)
    {
        return provider.HasCustomAttributes && provider.CustomAttributes.Any(static attribute =>
            attribute.AttributeType.FullName == typeof(CompilerGeneratedAttribute).FullName);
    }

    private static bool IsGeneratedInfrastructure(MethodDefinition method)
    {
        return method.DeclaringType.Name == "<Module>" ||
            method.DeclaringType.FullName == typeof(ProfilerHooks).FullName ||
            method.DeclaringType.Name.StartsWith("<MethodProfilerManifest", StringComparison.Ordinal);
    }

    private static IEnumerable<TypeDefinition> GetAllTypes(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types.Reverse());
        while (stack.Count != 0)
        {
            TypeDefinition type = stack.Pop();
            yield return type;
            for (int i = type.NestedTypes.Count - 1; i >= 0; i--)
            {
                stack.Push(type.NestedTypes[i]);
            }
        }
    }

    private static TypeDefinition? SafeResolve(TypeReference type)
    {
        try
        {
            return type.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    private static ulong ComputeMethodId(string identity)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(identity);
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= prime;
        }
        return hash == 0 ? 1 : hash;
    }

    private static void PreparePristineBackup(
        string assemblyPath,
        string? pdbPath,
        string backupAssemblyPath,
        string? backupPdbPath)
    {
        bool isWoven;
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath))
        {
            isWoven = HasWovenMarker(assembly);
        }

        if (!isWoven)
        {
            File.Copy(assemblyPath, backupAssemblyPath, true);
            if (pdbPath != null && backupPdbPath != null && File.Exists(pdbPath))
            {
                File.Copy(pdbPath, backupPdbPath, true);
            }
            else
            {
                DeleteIfExists(backupPdbPath);
            }
            return;
        }

        if (!File.Exists(backupAssemblyPath))
        {
            throw new InvalidOperationException(
                $"Assembly {assemblyPath} is already woven but its pristine backup is missing. Rebuild the project.");
        }
    }

    private static bool HasWovenMarker(AssemblyDefinition assembly)
    {
        return assembly.CustomAttributes.Any(static attribute =>
            attribute.AttributeType.FullName == typeof(MethodProfilerWovenAttribute).FullName);
    }

    private static void RestorePristine(
        string assemblyPath,
        string? pdbPath,
        string backupAssemblyPath,
        string? backupPdbPath)
    {
        File.Copy(backupAssemblyPath, assemblyPath, true);
        if (pdbPath != null && backupPdbPath != null && File.Exists(backupPdbPath))
        {
            File.Copy(backupPdbPath, pdbPath, true);
        }
    }

    private static void WriteReport(string reportPath, string assemblyName, List<string> lines, int includedCount)
    {
        string fullPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var output = new List<string>
        {
            $"Assembly: {assemblyName}",
            $"WeaverVersion: {WeaverVersion}",
            $"Included: {includedCount}",
        };
        output.AddRange(lines.Order(StringComparer.Ordinal));
        File.WriteAllLines(fullPath, output);
    }

    private static void DeleteIfExists(string? path)
    {
        if (path != null && File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
