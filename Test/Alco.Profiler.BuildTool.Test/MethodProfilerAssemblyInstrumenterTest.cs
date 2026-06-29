using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Alco.Profiler.BuildTool.Test;

public sealed class MethodProfilerAssemblyInstrumenterTest
{
    private sealed class IncludeFixtureRule : IMethodProfileRule
    {
        string IMethodProfileRule.Name => nameof(IncludeFixtureRule);

        MethodProfileRuleResult IMethodProfileRule.Evaluate(MethodProfileDescriptor method)
        {
            return method.IsSupported && !method.IsCompilerGenerated
                ? new MethodProfileRuleResult(MethodProfileDecision.Include)
                : default;
        }
    }

    [Test]
    public async Task InstrumentProducesReadableExecutableAssemblyAndPreservesPdb()
    {
        string sourceDirectory = TestContext.CurrentContext.TestDirectory;
        string temporaryDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "method-profiler-build-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        string assemblyPath = Path.Combine(temporaryDirectory, "Alco.Profiler.BuildTool.Fixture.dll");
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        string reportPath = Path.Combine(temporaryDirectory, "report.txt");
        File.Copy(Path.Combine(sourceDirectory, "Alco.Profiler.BuildTool.Fixture.dll"), assemblyPath);
        File.Copy(Path.Combine(sourceDirectory, "Alco.Profiler.BuildTool.Fixture.pdb"), pdbPath);

        var instrumenter = new MethodProfilerAssemblyInstrumenter([new IncludeFixtureRule()]);
        instrumenter.Instrument(assemblyPath, pdbPath, reportPath, [sourceDirectory]);
        instrumenter.Instrument(assemblyPath, pdbPath, reportPath, [sourceDirectory]);

        Assert.That(File.Exists(pdbPath), Is.True);
        Assert.That(File.ReadAllText(reportPath), Does.Contain("YieldAsync"));
        using (AssemblyDefinition rewritten = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
        {
            ReadSymbols = true,
            SymbolReaderProvider = new PortablePdbReaderProvider(),
        }))
        {
            Assert.That(rewritten.CustomAttributes.Any(attribute =>
                attribute.AttributeType.FullName == typeof(MethodProfilerInstrumentedAttribute).FullName), Is.True);
            MethodDefinition add = rewritten.MainModule.Types
                .SelectMany(static type => type.Methods)
                .Single(static method => method.Name == "Add");
            Assert.That(add.Body.Instructions.Any(static instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand is MethodReference called &&
                called.FullName.Contains("ProfilerHooks::Enter", StringComparison.Ordinal)), Is.True);
            Assert.That(add.Body.ExceptionHandlers.Any(static handler =>
                handler.HandlerType == ExceptionHandlerType.Finally), Is.True);
        }

        var loadContext = new AssemblyLoadContext("ProfilerBuildToolTest", true);
        Assembly loaded = loadContext.LoadFromAssemblyPath(assemblyPath);
        Type fixture = loaded.GetType("Alco.Profiler.BuildTool.Fixture.FixtureMethods", true)!;
        MethodInfo addMethod = fixture.GetMethod("Add", BindingFlags.Public | BindingFlags.Static)!;
        MethodInfo throwMethod = fixture.GetMethod("Throw", BindingFlags.Public | BindingFlags.Static)!;
        MethodInfo coalescedReturn = fixture.GetMethod("CoalescedReturn", BindingFlags.Public | BindingFlags.Static)!;
        MethodInfo yieldAsync = fixture.GetMethod("YieldAsync", BindingFlags.Public | BindingFlags.Static)!;
        MethodInfo yieldValues = fixture.GetMethod("YieldValues", BindingFlags.Public | BindingFlags.Static)!;
        Assert.That(addMethod.Invoke(null, [2, 3]), Is.EqualTo(5));
        Assert.That(coalescedReturn.Invoke(null, ["value"]), Is.EqualTo("value"));
        Assert.That(coalescedReturn.Invoke(null, [null]), Is.EqualTo(string.Empty));
        Assert.That(await (Task<int>)yieldAsync.Invoke(null, [7])!, Is.EqualTo(7));
        Assert.That(((IEnumerable<int>)yieldValues.Invoke(null, [3])!).ToArray(), Is.EqualTo(new[] { 0, 1, 2 }));
        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(() => throwMethod.Invoke(null, null))!;
        Assert.That(thrown.InnerException, Is.TypeOf<InvalidOperationException>());
        loadContext.Unload();
    }
}
