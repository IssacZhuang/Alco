using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>Verifies Slang native ownership, temporary pins, and managed result lifetimes.</summary>
[TestFixture]
[NonParallelizable]
public sealed class SlangLifetimeRegressionTest
{
    private const string Source = """
        cbuffer parameters : register(b0, space0) { float value; };
        RWStructuredBuffer<float> values : register(u0, space1);
        [shader("compute")]
        [numthreads(8, 4, 1)]
        void MainCS(uint3 id : SV_DispatchThreadID)
        {
            values[id.x] = value + float(id.y);
        }
        """;

    private static readonly string GenericSource = Source
        .Replace("void MainCS(", "void MainCS<let A : int, let B : int>(")
        .Replace("value +", "float(A + B) + value +");

    private static readonly IReadOnlyList<SlangEntryPointRequest> Entries =
        [new("MainCS", ShaderStage.Compute)];

    /// <summary>Verifies empty sources and includes remain valid with the SPIR-V 1.3 target.</summary>
    [Test]
    public void EmptySourceAndIncludeCompile()
    {
        Dictionary<string, string> files = new() { ["empty.slang"] = "" };
        using SlangCompiler compiler = new();
        using SlangCompileSession session = compiler.CreateSession(new SlangCompilerOptions
        {
            Resolver = path => files.GetValueOrDefault(
                Path.GetFileName(SlangPathUtility.NormalizePath(path))),
            Exists = path => files.ContainsKey(
                Path.GetFileName(SlangPathUtility.NormalizePath(path))),
        });
        SlangModuleHandle empty = session.LoadModuleFromSource("blank", "blank.slang", "");
        Assert.That(empty.GetEntryPointInfo(), Is.EqualTo((0, false)));

        SlangModuleHandle module = session.LoadModuleFromSource(
            "empty_include", "empty_include.slang", "#include \"empty.slang\"\n" + Source);
        using SlangProgram program = session.Compile(module, Entries);
        AssertProgram(program);
    }

    /// <summary>Verifies successful and partially failed UTF-8 initialization releases its pins.</summary>
    /// <param name="scenario">Successful search paths, partial search paths, or partial specialization arguments.</param>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void TemporaryUtf8PinsAreReleased(int scenario)
    {
        using SlangCompiler compiler = new();
        using SlangCompileSession session = compiler.CreateSession(SlangCompilerOptions.Default);
        SlangModuleHandle module = session.LoadModuleFromSource(
            "pin_arguments", "pin_arguments.slang", GenericSource);
        void Exercise()
        {
            if (scenario == 2)
            {
                Assert.Throws<ArgumentNullException>(() =>
                    session.Compile(module, Entries, ["1", null!]).Dispose());
                return;
            }
            if (scenario == 1)
            {
                Assert.Throws<ArgumentNullException>(() => compiler.CreateSession(
                    new SlangCompilerOptions { SearchPaths = ["first", null!] }).Dispose());
                return;
            }
            using SlangCompileSession temporary = compiler.CreateSession(
                new SlangCompilerOptions { SearchPaths = ["first", "second"] });
        }

        for (int i = 0; i < 4; i++)
            Exercise();
        long baseline = PinnedCount();
        for (int i = 0; i < 32; i++)
            Exercise();
        Assert.That(PinnedCount() - baseline, Is.LessThanOrEqualTo(4L),
            "Temporary UTF-8 pins must not accumulate once per operation.");

        using SlangProgram recovered = session.Compile(module, Entries, ["1", "2"]);
        AssertProgram(recovered);
    }

    /// <summary>Verifies failed session construction does not retain the resolver target.</summary>
    /// <param name="partialPin">Whether failure occurs after the first search path was pinned.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void FailedConstructionReleasesResolver(bool partialPin)
    {
        WeakReference target = CreateFailedResolver(partialPin);
        CollectGarbage();
        Assert.That(target.IsAlive, Is.False, "A failed constructor must release callback roots.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateFailedResolver(bool partialPin)
    {
        Dictionary<string, string> target = new();
        WeakReference reference = new(target);
        using SlangCompiler compiler = new();
        SlangCompilerOptions options = new()
        {
            Resolver = path => target.GetValueOrDefault(path),
            TargetProfile = partialPin ? null : "__missing_lifetime_profile__",
            SearchPaths = partialPin ? ["first", null!] : [],
        };
        TestDelegate attempt = () => compiler.CreateSession(options).Dispose();
        if (partialPin)
            Assert.Throws<ArgumentNullException>(attempt);
        else
            Assert.Throws<ArgumentException>(attempt);
        return reference;
    }

    /// <summary>Verifies entry-point inspection releases every acquired native reference.</summary>
    /// <param name="generic">Whether the first entry triggers the generic early-exit path.</param>
    [TestCase(false)]
    [TestCase(true)]
    public unsafe void EntryPointInfoDoesNotIncreaseReferenceCount(bool generic)
    {
        using SlangCompiler compiler = new();
        using SlangCompileSession session = compiler.CreateSession(SlangCompilerOptions.Default);
        string source = (generic ? GenericSource : Source) + """

            [shader("compute")]
            [numthreads(1, 1, 1)]
            void OtherCS() {}
            """;
        SlangModuleHandle module = session.LoadModuleFromSource(
            "entry_references", "entry_references.slang", source);
        lock (SlangNative.NativeLock)
        {
            SlangEntryPoint entry = module.Native.GetDefinedEntryPoint(0)
                ?? throw new AssertionException("The module must expose its first entry.");
            try
            {
                uint before = ReferenceCount(entry.NativePointer);
                for (int i = 0; i < 10; i++)
                    Assert.That(module.GetEntryPointInfo(), Is.EqualTo((2, generic)));
                Assert.That(ReferenceCount(entry.NativePointer), Is.EqualTo(before));
            }
            finally
            {
                entry.Release();
            }
        }
    }

    private static unsafe uint ReferenceCount(IntPtr pointer)
    {
        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Com.Vcall(pointer, 1))(pointer);
        return ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Com.Vcall(pointer, 2))(pointer);
    }

    /// <summary>Verifies module ownership checks and session-independent program results.</summary>
    [Test]
    public void DisposedModulesRejectAccessAndProgramsRemainManaged()
    {
        using SlangCompiler compiler = new();
        using SlangCompileSession owner = compiler.CreateSession(SlangCompilerOptions.Default);
        using SlangCompileSession other = compiler.CreateSession(SlangCompilerOptions.Default);
        SlangModuleHandle module = owner.LoadModuleFromSource("owned", "owned.slang", Source);
        SlangModuleHandle local = other.LoadModuleFromSource("local", "local.slang", Source);
        using SlangProgram program = owner.Compile(module, Entries);
        byte[] expectedCode = program.EntryCode[0].ToArray();

        AssertRejected<ArgumentException>(other, module, local);
        owner.Dispose();
        AssertDisposed(module);
        AssertRejected<ObjectDisposedException>(other, module, local);
        program.Dispose();
        program.Dispose();
        AssertProgram(program);
        Assert.That(program.EntryCode[0], Is.EqualTo(expectedCode));
    }

    private static void AssertRejected<TException>(
        SlangCompileSession session, SlangModuleHandle rejected, SlangModuleHandle local)
        where TException : Exception
    {
        TestDelegate[] calls =
        [
            () => session.Compile(rejected, Entries).Dispose(),
            () => session.Compile(rejected, Entries, []).Dispose(),
            () => session.CompileAllEntryPoints(rejected, []).Dispose(),
            () => _ = session.GetModuleReflection(rejected),
            () => session.CompileComposed(rejected, local, []).Dispose(),
            () => session.CompileComposed(local, rejected, []).Dispose(),
        ];
        for (int i = 0; i < calls.Length; i++)
            Assert.Throws<TException>(calls[i], $"Module validation entry {i}.");
    }

    private static void AssertDisposed(SlangModuleHandle module)
    {
        TestDelegate[] calls =
        [
            () => _ = module.Name,
            () => _ = module.FilePath,
            () => _ = module.GetDependencyFilePaths(),
            () => _ = module.GetEntryPointInfo(),
            () => _ = module.Serialize(),
            () => _ = module.GetModuleDecl(),
        ];
        for (int i = 0; i < calls.Length; i++)
            Assert.Throws<ObjectDisposedException>(calls[i], $"Disposed metadata entry {i}.");
    }

    /// <summary>Verifies independent systems can compile, inspect, invalidate, and dispose concurrently.</summary>
    /// <param name="normalize">Whether SPIR-V normalization for Naga is enabled.</param>
    [TestCase(false)]
    [TestCase(true)]
    public async Task IndependentSystemsRemainSafeDuringConcurrentLifetimes(bool normalize)
    {
        ManualResetEventSlim start = new();
        Task[] workers = new Task[4];
        for (int i = 0; i < workers.Length; i++)
        {
            int index = i;
            workers[i] = Task.Run(() => RunSystems(index, normalize, start));
        }
        Task completion = Task.WhenAll(workers);
        start.Set();
        try
        {
            Task finished = await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(90)));
            Assert.That(finished, Is.SameAs(completion),
                "Slang workers timed out; unfinished workers retain ownership of their resources.");
            await completion;
        }
        finally
        {
            _ = completion.ContinueWith(finished =>
            {
                _ = finished.Exception;
                start.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static async Task RunSystems(int index, bool normalize, ManualResetEventSlim start)
    {
        if (!start.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Slang workers did not start.");
        for (int round = 0; round < 4; round++)
        {
            uint width = (uint)(index + 1);
            string name = $"lifetime_{index}_{round}";
            string path = $"{name}.slang";
            string source = ("import lifetime_lib;\n" + Source)
                .Replace("numthreads(8,", $"numthreads({width},")
                .Replace("value +", "Adjustment() + value +");
            Dictionary<string, string> files = new()
            {
                [path] = source,
                ["lifetime_lib.slang"] = "export float Adjustment() { return 1.0; }",
            };
            SlangProgram retained;
            using (SlangModuleSystem system = new(new SlangCompilerOptions
            {
                NormalizeSpirvForNaga = normalize,
                Resolver = probe => files.GetValueOrDefault(
                    Path.GetFileName(SlangPathUtility.NormalizePath(probe))),
                Exists = probe => files.ContainsKey(
                    Path.GetFileName(SlangPathUtility.NormalizePath(probe))),
            }, null))
            {
                SlangModuleHandle module = system.GetOrLoadModule(name, path, source);
                Assert.That(module.Name, Is.EqualTo(name));
                Assert.That(module.FilePath, Does.EndWith(path));
                Assert.That(module.GetDependencyFilePaths(), Has.Some.Contains("lifetime_lib"));
                Assert.That(module.GetEntryPointInfo(), Is.EqualTo((1, false)));
                Assert.That(module.Serialize(), Is.Not.Null.And.Not.Empty);
                Assert.That(module.GetModuleDecl(), Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(system.GetModuleReflection(name).UniformBlocks
                    .Single(block => block.Name == "parameters").Members.Single().Name,
                    Is.EqualTo("value"));
                retained = system.GetProgramAllEntries(name, []);
                AssertProgram(retained, width);
                retained.Dispose();
                Assert.That(system.GetProgramAllEntries(name, []), Is.SameAs(retained));
                string dependency = system.GetModuleDependencies(name)
                    .First(file => file.Contains("lifetime_lib", StringComparison.Ordinal));

                using Barrier race = new(2);
                Task dispose = StartWorker(() =>
                {
                    WaitForRound(race);
                    for (int i = 0; i < 16; i++)
                        retained.Dispose();
                });
                Task invalidate = StartWorker(() =>
                {
                    WaitForRound(race);
                    Assert.That(system.InvalidateModulesContaining(dependency), Does.Contain(name));
                });
                // Native resources must outlive both workers, including after the outer test times out.
                await Task.WhenAll(dispose, invalidate);
                AssertDisposed(module);
                AssertProgram(retained, width);
                SlangModuleHandle replacement = system.GetOrLoadModule(name, path, source);
                Assert.That(replacement, Is.Not.SameAs(module));
                using SlangProgram fresh = system.GetProgramAllEntries(name, []);
                AssertProgram(fresh, width);
            }
            retained.Dispose();
            AssertProgram(retained, width);
        }
    }

    private static Task StartWorker(Action action) => Task.Factory.StartNew(
        action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static void WaitForRound(Barrier barrier)
    {
        if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Concurrent disposal and invalidation did not rendezvous.");
    }

    private static void AssertProgram(SlangProgram program, uint width = 8)
    {
        Assert.That(program.EntryCode, Has.Length.EqualTo(1));
        Assert.That(program.EntryPoints.Single().Name, Is.EqualTo("MainCS"));
        Assert.That(program.EntryCode[0].Length, Is.GreaterThan(8));
        Assert.That(BitConverter.ToUInt32(program.EntryCode[0], 0), Is.EqualTo(0x07230203u));
        Assert.That(BitConverter.ToUInt32(program.EntryCode[0], 4), Is.EqualTo(0x00010300u));
        Assert.That(program.Reflection.Size, Is.EqualTo(new ThreadGroupSize(width, 4, 1)));
        Assert.That(program.Reflection.BindGroups, Has.Count.EqualTo(2));
        Assert.That(program.Reflection.BindGroups[1].Bindings.Single().Entry.Type,
            Is.EqualTo(BindingType.StorageBuffer));
        ShaderUniformMember member = program.GetUniformMembers("parameters").Single();
        Assert.That((member.Name, member.OffsetBytes, member.ComponentCount),
            Is.EqualTo(("value", 0u, 1)));
    }

    private static long PinnedCount()
    {
        CollectGarbage();
        return GC.GetGCMemoryInfo().PinnedObjectsCount;
    }

    private static void CollectGarbage()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
