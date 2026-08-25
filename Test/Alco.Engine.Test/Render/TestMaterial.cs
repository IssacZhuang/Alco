using System.Threading;
using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine.Test;

public class TestMaterial
{
    [Test]
    public void TestMaterialInheritance()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        Shader shader = engine.BuiltInAssets.Shader_Sprite;
        GraphicsMaterial material = renderingSystem.CreateGraphicsMaterial(shader, "root", false);
        GraphicsBuffer camera = renderingSystem.CreateCamera2D(1280, 720, 1000);

        material.SetBuffer(0, camera);

        GraphicsMaterialInstance instance1 = material.CreateInstance();

        // Bind groups are assembled per material from the slot values; the instance
        // resolves unbound values from the parent chain, so its group is complete
        // even though it set nothing itself.
        Assert.IsTrue(instance1[0] != null);

        instance1.SetTexture(1, renderingSystem.TextureWhite);

        GraphicsMaterialInstance instance2 = instance1.CreateInstance();
        GraphicsMaterialInstance instance3 = instance1.CreateInstance();

        instance2.SetTexture(1, renderingSystem.TextureBlack);

        // The override (black) and the inherited value (white) assemble to
        // different groups.
        Assert.IsTrue(instance2[1] != null);
        Assert.IsTrue(instance3[1] != null);
        Assert.IsTrue(instance2[1] != instance3[1]);

        // Unbound slots fall back to the parent chain in every group.
        Assert.IsTrue(instance2[0] != null);
        Assert.IsTrue(instance3[0] != null);

        // Unchanged contents are served from the content cache: repeated access
        // returns the same group object instead of rebuilding it.
        Assert.AreSame(instance2[0], instance2[0]);
        Assert.AreSame(instance3[1], instance3[1]);
    }

    [Test]
    public void TestCameraBufferFlushedOnGroupAssembly()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        Shader shader = engine.BuiltInAssets.Shader_Sprite;
        GraphicsMaterial material = renderingSystem.CreateGraphicsMaterial(shader, "root", false);
        Camera2DBuffer camera = renderingSystem.CreateCamera2D(1280, 720, 1000);
        camera.Position = new System.Numerics.Vector2(100, 100);

        // The camera has a pending matrix upload (dirty) which used to be flushed by
        // SetBuffer through the EntryReadonly getter. The bind group assembly must
        // preserve that flush, otherwise the bound camera UBO stays zero.
        Assert.IsTrue(IsDirty(camera));

        material.SetBuffer(0, camera);
        Assert.IsTrue(material[0] != null);
        Assert.IsFalse(IsDirty(camera));

        // The instance resolves the camera from the parent chain when assembling,
        // so the pending matrix change is picked up without rebinding.
        GraphicsMaterialInstance instance = material.CreateInstance();
        camera.Position = new System.Numerics.Vector2(200, 200);
        Assert.IsTrue(IsDirty(camera));
        Assert.IsTrue(instance[0] != null);
        Assert.IsFalse(IsDirty(camera));
    }

    [Test]
    public void TestInstanceTracksParentChanges()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        Shader shader = engine.BuiltInAssets.Shader_Sprite;
        GraphicsMaterial material = renderingSystem.CreateGraphicsMaterial(shader, "root", false);
        material.SetBuffer(0, renderingSystem.CreateCamera2D(640, 360, 100));

        GraphicsMaterialInstance instance = material.CreateInstance();
        GPUResourceGroup? before = instance[0];
        Assert.IsTrue(before != null);

        // Changing only the parent must be picked up by the instance (tracked
        // through the fallback chain versions) without any set on the instance.
        material.SetBuffer(0, renderingSystem.CreateCamera2D(1280, 720, 100));
        GPUResourceGroup? after = instance[0];
        Assert.IsTrue(after != null);
        Assert.AreNotSame(before, after);

        // Steady state: no change, no reassembly.
        Assert.AreSame(after, instance[0]);
    }

    [Test]
    public void TestSharedSamplerBankGroupIsEngineWideImmutable()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;

        // Two core-importing shaders with no resources in common: their bank
        // groups must still be the very same engine-wide GPUResourceGroup.
        GraphicsMaterial sprite = renderingSystem.CreateGraphicsMaterial(engine.BuiltInAssets.Shader_Sprite, "sprite", false);
        GraphicsMaterial blit = renderingSystem.CreateGraphicsMaterial(engine.BuiltInAssets.Shader_Blit, "blit");

        GPUResourceGroup? BankGroup(GraphicsMaterial material)
        {
            ShaderReflectionInfo reflection = material.ReflectionInfo;
            for (int g = 0; g < reflection.BindGroups.Count; g++)
            {
                IReadOnlyList<BindGroupEntryInfo> bindings = reflection.BindGroups[g].Bindings;
                bool allBank = bindings.Count > 0;
                for (int e = 0; e < bindings.Count; e++)
                {
                    BindGroupEntry entry = bindings[e].Entry;
                    if (entry.Type is not (BindingType.Sampler or BindingType.SamplerComparison)
                        || !renderingSystem.Samplers.IsBankMember(entry.Name))
                    {
                        allBank = false;
                        break;
                    }
                }
                if (allBank)
                {
                    return material[g];
                }
            }
            return null;
        }

        GPUResourceGroup? spriteBank = BankGroup(sprite);
        GPUResourceGroup? blitBank = BankGroup(blit);
        Assert.IsTrue(spriteBank != null, "The sprite shader has a sampler bank group.");
        Assert.IsTrue(blitBank != null, "The blit shader has a sampler bank group.");
        Assert.IsTrue(ReferenceEquals(spriteBank, blitBank),
            "The bank group is one shared engine-wide instance across shaders, not per material.");

        // The bank is immutable: its member names never accept a binding.
        using GPUSampler attempt = renderingSystem.GraphicsDevice.CreateSampler(new SamplerDescriptor(
            FilterMode.Nearest, FilterMode.Nearest, FilterMode.Nearest,
            AddressMode.Repeat, AddressMode.Repeat, AddressMode.Repeat, name: "attempt"));
        Assert.IsFalse(sprite.TrySetSampler("_linearClamp", attempt));
    }

    [Test]
    public void TestMaterialsOnMultipleThreadsAreSafe()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;

        const int threadCount = 4;
        const int iterations = 32;
        System.Collections.Concurrent.ConcurrentQueue<Exception> errors = new();
        GPUResourceGroup?[] bankGroups = new GPUResourceGroup?[threadCount];
        Barrier start = new Barrier(threadCount);

        // Prime the shader's first specialization on the main thread so the
        // worker threads race only through the engine-side caches (module and
        // program caches, bank group cache, per-resource group caches).
        // The sprite module's entry points are generic — like every other sprite
        // test, the material pins the specialization explicitly.
        using (GraphicsMaterial primer = renderingSystem.CreateGraphicsMaterial(
            engine.BuiltInAssets.Shader_Sprite, "primer", false))
        {
            Assert.IsTrue(primer[0] != null);
        }

        static GPUResourceGroup? FindBankGroup(RenderingSystem renderingSystem, GraphicsMaterial material)
        {
            ShaderReflectionInfo reflection = material.ReflectionInfo;
            for (int g = 0; g < reflection.BindGroups.Count; g++)
            {
                IReadOnlyList<BindGroupEntryInfo> bindings = reflection.BindGroups[g].Bindings;
                bool allBank = bindings.Count > 0;
                for (int e = 0; e < bindings.Count; e++)
                {
                    BindGroupEntry entry = bindings[e].Entry;
                    if (entry.Type is not (BindingType.Sampler or BindingType.SamplerComparison)
                        || !renderingSystem.Samplers.IsBankMember(entry.Name))
                    {
                        allBank = false;
                        break;
                    }
                }
                if (allBank)
                {
                    return material[g];
                }
            }
            return null;
        }

        Thread[] threads = new Thread[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int index = t;
            threads[t] = new Thread(() =>
            {
                try
                {
                    // Align the threads so they hit the shared caches together:
                    // the sampler bank's group cache, the shader module/pipeline
                    // caches and the texture's per-layout group cache.
                    start.SignalAndWait();

                    using GraphicsBuffer camera = renderingSystem.CreateCamera2D(1280, 720, 100);
                    for (int i = 0; i < iterations; i++)
                    {
                        GraphicsMaterial material = renderingSystem.CreateGraphicsMaterial(
                            engine.BuiltInAssets.Shader_Sprite, $"mt_{index}_{i}", false);
                        material.SetBuffer(0, camera);
                        material.SetTexture(1, renderingSystem.TextureWhite);
                        for (int g = 0; g < material.ReflectionInfo.BindGroups.Count; g++)
                        {
                            GPUResourceGroup? group = material[g];
                            Assert.IsTrue(group != null,
                                $"Thread {index} iteration {i}: group {g} did not assemble.");
                        }

                        bankGroups[index] ??= FindBankGroup(renderingSystem, material);
                    }
                }
                catch (Exception e)
                {
                    errors.Enqueue(e);
                }
            });
            threads[t].Start();
        }

        for (int t = 0; t < threadCount; t++)
        {
            threads[t].Join();
        }

        Assert.IsTrue(errors.IsEmpty, $"Concurrent material use failed: {string.Join("; ", errors)}");
        for (int t = 0; t < threadCount; t++)
        {
            Assert.IsTrue(bankGroups[t] != null, $"Thread {t} did not resolve a sampler bank group.");
            Assert.IsTrue(ReferenceEquals(bankGroups[0], bankGroups[t]),
                "The bank group is one shared engine-wide instance across threads.");
        }
    }

    [Test]
    public void TestFlushSteadyStateNoAllocation()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        Shader shader = engine.BuiltInAssets.Shader_Sprite;
        GraphicsMaterial material = renderingSystem.CreateGraphicsMaterial(shader, "root", false);
        material.SetBuffer(0, renderingSystem.CreateCamera2D(1280, 720, 100));
        GraphicsMaterialInstance instance = material.CreateInstance();
        instance.SetTexture(1, renderingSystem.TextureWhite);

        // Warm up: assemble the groups and let the JIT settle.
        for (int i = 0; i < 100_000; i++)
        {
            _ = instance[0];
            _ = instance[1];
        }

        // The per-draw flush is on the hot path: with unchanged values it must be
        // a few integer comparisons per group and allocate nothing. The first
        // measured chunk absorbs one-time runtime effects; the second must be 0.
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            _ = instance[0];
            _ = instance[1];
        }
        long mid = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            _ = instance[0];
            _ = instance[1];
        }
        long after = System.GC.GetAllocatedBytesForCurrentThread();
        TestContext.WriteLine($"chunk1: {mid - before} B, chunk2: {after - mid} B");
        Assert.AreEqual(0, after - mid);
    }

    private static bool IsDirty(Camera2DBuffer camera)
    {
        System.Reflection.FieldInfo? field = typeof(BaseCameraBuffer<CameraData2D>)
            .GetField("_dirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        return (bool)field!.GetValue(camera)!;
    }
}
