using Alco.Graphics;
using Alco.Graphics.AlcoGpu;
using Alco.Graphics.NoGPU;
#nullable enable

using NUnit.Framework;

namespace Alco.Rendering.Test;

// ─────────────────────────────────────────────────────────────────────────────
// ShaderSystem tests: module-name keyed Shader creation through the provider
// seam, specialization identity, and hot-reload
// invalidation wired to Shader version bumps. Most tests use NoGPU; the DX12
// backend regressions execute and read back results through a real device.
// ─────────────────────────────────────────────────────────────────────────────
/// <summary>Verifies module-backed shader compilation, backend targets and invalidation.</summary>
[TestFixture]
public class ShaderSystemTest
{
    private const string QuadModule = """
        import AlcoRendering_Core;

        cbuffer camera : register(b0, space0)
        {
            float4x4 viewProjection;
        };

        Texture2D albedo        : register(t0, space1);
        SamplerState linearClamp : register(s0, space1);

        struct Vertex
        {
            float3 position : POSITION;
            float2 uv       : TEXCOORD0;
        };

        struct V2F
        {
            float4 position : SV_POSITION;
            float2 uv       : TEXCOORD0;
        };

        [shader("vertex")]
        V2F MainVS(Vertex input)
        {
            V2F output;
            output.position = mul(viewProjection, float4(input.position, 1.0));
            output.uv = input.uv;
            return output;
        }

        [shader("fragment")]
        float4 MainPS(V2F input) : SV_TARGET
        {
            // Prove the import graph is live: core's helper and constant.
            float dither = outputDither8Bit(input.position.xy) * 0.0;
            return sampleTex2D(albedo, linearClamp, input.uv) + dither + PI * 0.0;
        }
        """;

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Alco.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static SlangCompilerOptions OptionsWithQuadModule()
    {
        string corePath = Path.Combine(
            RepoRoot(), "Src", "Alco.Rendering", "Assets", "Shaders", "Libs", "AlcoRendering_Core.slang");
        string core = File.ReadAllText(corePath);
        return new SlangCompilerOptions
        {
            Resolver = path =>
            {
                // Imports probe several module-name→file forms ('a/b.slang',
                // 'a-b.slang', …); match on the dashed form.
                string key = SlangPathUtility.NormalizePath(path).Replace('/', '-');
                if (key.EndsWith("AlcoRendering_Core.slang", StringComparison.OrdinalIgnoreCase))
                    return core;
                if (key.EndsWith("alco-sandbox-quad.slang", StringComparison.OrdinalIgnoreCase))
                    return QuadModule;
                return null;
            },
        };
    }

    private sealed class Dx12NoDevice : NoDevice
    {
        /// <inheritdoc />
        public override GraphicsBackend Backend => GraphicsBackend.WGPUDx12;
    }

    private sealed class GpuHost : IGPUDeviceHost, IDisposable
    {
        /// <inheritdoc />
        public event Action OnEndFrame { add { } remove { } }
        /// <inheritdoc />
        public event Action? OnDispose;
        /// <inheritdoc />
        public void Dispose() => OnDispose?.Invoke();
        /// <inheritdoc />
        public void LogInfo(ReadOnlySpan<char> message) { }
        /// <inheritdoc />
        public void LogWarning(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());
        /// <inheritdoc />
        public void LogError(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());
        /// <inheritdoc />
        public void LogSuccess(ReadOnlySpan<char> message) { }
    }

    /// <summary>Ensures DX12 shader compilation retains Vulkan instance semantics in SPIR-V.</summary>
    [Test]
    public void Dx12Backend_CompilesVulkanInstanceIdToSpirv()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem(device: new Dx12NoDevice());
        Shader shader = host.RenderingSystem.ShaderSystem.GetShaderFromModule(
            "dx12_instance_target", "dx12_instance_target.slang", """
                [shader("vertex")]
                float4 MainVS(uint id : SV_VulkanInstanceID) : SV_POSITION
                {
                    return float4(float(id), 0, 0, 1);
                }
                [shader("fragment")]
                float4 MainPS() : SV_TARGET { return float4(1, 0, 0, 1); }
                """);
        ShaderModule vertex = shader.GetShaderModules().VertexShader!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(host.RenderingSystem.ShaderSystem.Modules.Target, Is.EqualTo(SlangCodeTarget.Spirv));
            Assert.That(vertex.Language, Is.EqualTo(ShaderLanguage.SPIRV));
            Assert.That(vertex.EntryPoint, Is.EqualTo("main"));
            Assert.That(BitConverter.ToUInt32(vertex.Source.Span), Is.EqualTo(0x07230203u));
        });
    }

    /// <summary>Checks translated DX12 builtins and immediates with nonzero draw offsets.</summary>
    /// <param name="indirect">Whether to execute an indexed indirect draw.</param>
    /// <param name="debug">Whether to enable native debugging and validation.</param>
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    [Category("AlcoGpu")]
    [NonParallelizable]
    public unsafe void Dx12VulkanBuiltins_IncludeDrawOffsets(bool indirect, bool debug)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Direct3D 12 requires Windows.");
        }
        using GpuHost gpuHost = new();
        var device = new AlcoGpuDevice(new DeviceDescriptor(gpuHost, GraphicsBackend.WGPUDx12, debug: debug));
        Assert.That(device.IsFeatureSupported(GPUFeatures.IndirectFirstInstance), Is.True,
            "The translated shader must preserve a nonzero indirect firstInstance.");
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem(device: device);
        Shader shader = host.RenderingSystem.ShaderSystem.GetShaderFromModule(
            "dx12_draw_offsets", "dx12_draw_offsets.slang", """
                struct Constants { uint expectedInstance; uint firstVertex; };
                [[vk::push_constant]] Constants constants;
                struct V2F
                {
                    float4 position : SV_POSITION;
                    nointerpolation uint instance : TEXCOORD0;
                };
                [shader("vertex")]
                V2F MainVS(uint vertex : SV_VulkanVertexID, uint instance : SV_VulkanInstanceID)
                {
                    float2 positions[3] = { float2(-1, -1), float2(3, -1), float2(-1, 3) };
                    V2F output;
                    output.position = float4(positions[vertex - constants.firstVertex], 0, 1);
                    output.instance = instance;
                    return output;
                }
                [shader("fragment")]
                float4 MainPS(V2F input) : SV_TARGET
                {
                    return input.instance == constants.expectedInstance
                        ? float4(0, 1, 0, 1) : float4(1, 0, 0, 1);
                }
                """);
        ShaderModulesInfo modules = shader.GetShaderModules();
        Assert.That(modules.ReflectionInfo.BindGroups, Is.Empty);
        Assert.That(modules.ReflectionInfo.PushConstantsSize, Is.EqualTo(8u));
        using GPUPipeline pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDescriptor(
            [], [modules.VertexShader!.Value, modules.FragmentShader!.Value], [],
            RasterizerState.CullNone, BlendState.Opaque, DepthStencilState.None,
            PrimitiveTopology.TriangleList, [PixelFormat.RGBA8Unorm], null, pushConstantsSize: 8));
        var layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment { Format = PixelFormat.RGBA8Unorm, ClearColor = new(0, 0, 0, 1) }], null));
        using var frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16));
        using GPUBuffer indices = device.CreateBuffer(new BufferDescriptor(12, BufferUsage.Index | BufferUsage.CopyDst));
        uint* indexData = stackalloc uint[3] { 0, 1, 2 };
        device.WriteBuffer(indices, 0, (byte*)indexData, 12);
        using GPUBuffer arguments = device.CreateBuffer(new BufferDescriptor(20, BufferUsage.Indirect | BufferUsage.CopyDst));
        IndexedIndirectData draw = new(3, 1, 0, 5, 7);
        device.WriteBuffer(arguments, 0, (byte*)&draw, 20);
        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        using (var pass = commands.BeginRender(frameBuffer))
        {
            pass.SetPipeline(pipeline);
            pass.PushConstants((7u, 5u));
            if (indirect)
            {
                pass.SetIndexBuffer(indices, IndexFormat.UInt32);
                pass.DrawIndexedIndirect(arguments, 0);
            }
            else
            {
                pass.Draw(3, 1, 5, 7);
            }
        }
        commands.End();
        device.Submit(commands);
        byte[] pixels = new byte[16 * 16 * 4];
        fixed (byte* destination = pixels)
        {
            device.ReadTexture(frameBuffer.Colors[0], destination, (uint)pixels.Length);
        }
        Assert.That(pixels[0..4], Is.EqualTo(new byte[] { 0, 255, 0, 255 }));
        Assert.That(pixels[^4..], Is.EqualTo(new byte[] { 0, 255, 0, 255 }));
        layout.Destroy();
    }

    /// <summary>Checks translated DX12 vertex orientation against a top-left scissor rectangle.</summary>
    /// <param name="debug">Whether native debugging and validation are enabled.</param>
    [TestCase(false)]
    [TestCase(true)]
    [Category("AlcoGpu")]
    [NonParallelizable]
    public unsafe void Dx12AsymmetricTriangle_RespectsTopLeftScissor(bool debug)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Direct3D 12 requires Windows.");
        }
        using GpuHost gpuHost = new();
        var device = new AlcoGpuDevice(new DeviceDescriptor(gpuHost, GraphicsBackend.WGPUDx12, debug: debug));
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem(device: device);
        Shader shader = host.RenderingSystem.ShaderSystem.GetShaderFromModule(
            "dx12_asymmetric_scissor", "dx12_asymmetric_scissor.slang", """
                [shader("vertex")]
                float4 MainVS(uint vertex : SV_VulkanVertexID) : SV_POSITION
                {
                    float2 positions[3] = { float2(-1, 0), float2(3, 0), float2(-1, 2) };
                    return float4(positions[vertex], 0, 1);
                }
                [shader("fragment")]
                float4 MainPS() : SV_TARGET { return float4(0, 1, 0, 1); }
                """);
        ShaderModulesInfo modules = shader.GetShaderModules();
        using GPUPipeline pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDescriptor(
            [], [modules.VertexShader!.Value, modules.FragmentShader!.Value], [],
            RasterizerState.CullNone, BlendState.Opaque, DepthStencilState.None,
            PrimitiveTopology.TriangleList, [PixelFormat.RGBA8Unorm], null));
        var layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment { Format = PixelFormat.RGBA8Unorm, ClearColor = new(0, 0, 0, 1) }], null));
        using var frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16));
        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        using (var pass = commands.BeginRender(frameBuffer, new System.Numerics.Vector4(0, 0, 0, 1)))
        {
            pass.SetPipeline(pipeline);
            pass.SetScissorRect(0, 0, 16, 8);
            pass.Draw(3, 1, 0, 0);
        }
        commands.End();
        device.Submit(commands);
        byte[] pixels = new byte[16 * 16 * 4];
        fixed (byte* destination = pixels)
        {
            device.ReadTexture(frameBuffer.Colors[0], destination, (uint)pixels.Length);
        }
        const int topPixel = (1 * 16 + 1) * 4;
        const int bottomPixel = (12 * 16 + 1) * 4;
        Assert.Multiple(() =>
        {
            Assert.That(pixels[topPixel..(topPixel + 4)], Is.EqualTo(new byte[] { 0, 255, 0, 255 }),
                "Pixel (1, 1) must be inside both the upper-half triangle and the top-left scissor.");
            Assert.That(pixels[bottomPixel..(bottomPixel + 4)], Is.EqualTo(new byte[] { 0, 0, 0, 255 }),
                "Pixel (1, 12) must retain the explicit clear color outside the scissor.");
        });
        layout.Destroy();
    }

    /// <summary>Verifies translated Slang loop exits, nested loops, explicit breaks and sequential continues.</summary>
    /// <param name="debug">Whether native debugging and validation are enabled.</param>
    [TestCase(false)]
    [TestCase(true)]
    [Category("AlcoGpu")]
    [NonParallelizable]
    public unsafe void Dx12ComputeLoops_TerminateAndProduceExpectedSums(bool debug)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Direct3D 12 requires Windows.");
        }
        using GpuHost gpuHost = new();
        var device = new AlcoGpuDevice(new DeviceDescriptor(gpuHost, GraphicsBackend.WGPUDx12, debug: debug));
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem(device: device);
        Shader shader = host.RenderingSystem.ShaderSystem.GetShaderFromModule(
            "dx12_compute_loop_exits", "dx12_compute_loop_exits.slang", """
                struct OutputParams { RWStructuredBuffer<uint4> output; };
                ParameterBlock<OutputParams> pass;
                struct Constants { uint limit; uint stop; };
                [[vk::push_constant]] Constants constants;
                // Keep the guarded continue's ancestor merge free of scalar accumulator spills.
                void Increment(inout uint value) { value++; }
                uint4 CountLoopIterations(uint limit, uint stop)
                {
                    uint count = 0;
                    uint nested = 0;
                    uint broken = 0;
                    for (uint i = 0; i < limit; i++)
                    {
                        count++;
                        for (uint j = 0; j < limit; j++)
                        {
                            for (uint k = 0; k < limit; k++)
                            {
                                if (k < 3)
                                {
                                    if (k == 1) continue;
                                    Increment(nested);
                                }
                            }
                        }
                    }
                    for (uint i = 0; i < limit; i++)
                    {
                        if (i == stop) break;
                        broken++;
                    }
                    uint continued = 0;
                    for (uint i = 0; i < limit; i++)
                    {
                        if (i == 1) continue;
                        if (i == 3) continue;
                        continued += i + 1;
                    }
                    return uint4(count, nested, broken, continued);
                }
                [shader("compute")][numthreads(1, 1, 1)]
                void MainCS()
                {
                    uint limit = constants.limit;
                    uint stop = constants.stop;
                    pass.output[0] = CountLoopIterations(limit, stop);
                }
                """);
        using var output = host.RenderingSystem.CreateGraphicsArrayBuffer<uint4>(1);
        output.UpdateBuffer();
        using var material = host.RenderingSystem.CreateComputeMaterial(shader);
        material.SetBuffer("output", output);
        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        using (var pass = commands.BeginCompute())
        {
            material.DispatchByGroupWithConstant(pass, 1, 1, 1, new uint2(4, 2));
        }
        commands.End();
        device.Submit(commands);
        uint4 result = default;
        device.ReadBuffer(output.NativeBuffer, (byte*)&result, 0, 16);
        Assert.That(result, Is.EqualTo(new uint4(4, 32, 2, 4)));
    }

    /// <summary>Compiles a module and its imports through the shader provider.</summary>
    [Test]
    public void GetShader_CompilesModuleWithImports()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, OptionsWithQuadModule(), cacheDirectory: null);

        Shader shader = shaderSystem.GetShader("alco-sandbox-quad");
        ShaderModulesInfo modules = shader.GetShaderModules();

        Assert.Multiple(() =>
        {
            Assert.That(modules.IsGraphicsShader, Is.True);
            Assert.That(modules.IsComputeShader, Is.False);
            Assert.That(modules.VertexShader, Is.Not.Null);
            Assert.That(modules.FragmentShader, Is.Not.Null);
            Assert.That(modules.VertexShader!.Value.Source.Length, Is.GreaterThan(4));
            // Name-based binding surface survives (D1): the engine resolves by name.
            Assert.That(modules.ReflectionInfo.TryGetResourceId("camera", out _), Is.True);
            Assert.That(modules.ReflectionInfo.TryGetResourceId("albedo", out _), Is.True);
            Assert.That(modules.ReflectionInfo.VertexLayouts.Count, Is.EqualTo(1));
            // The core module is part of the dependency graph.
            Assert.That(shaderSystem.Modules.GetModuleDependencies("alco-sandbox-quad"),
                Has.Some.Contains("AlcoRendering_Core.slang"));
        });
    }

    /// <summary>Ensures repeated module lookups return the same shader handle.</summary>
    [Test]
    public void GetShader_CachesPerModuleAndSpecialization()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, OptionsWithQuadModule(), cacheDirectory: null);

        Shader a = shaderSystem.GetShader("alco-sandbox-quad");
        Shader b = shaderSystem.GetShader("alco-sandbox-quad");

        Assert.That(b, Is.SameAs(a), "same (module, specialization) must return the same Shader");
    }

    /// <summary>Checks dependent shader invalidation when an imported module changes.</summary>
    [Test]
    public void Invalidate_CoreModule_BumpsShaderVersionAndFiresEvent()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, OptionsWithQuadModule(), cacheDirectory: null);
        Shader shader = shaderSystem.GetShader("alco-sandbox-quad");
        uint versionBefore = shader.Version;
        List<Shader> invalidated = [];
        shaderSystem.ShaderInvalidated += invalidated.Add;

        string coreDep = shaderSystem.Modules.GetModuleDependencies("alco-sandbox-quad")
            .First(dep => dep.Contains("AlcoRendering_Core.slang"));
        IReadOnlyList<string> affected = shaderSystem.InvalidateModulesContaining(coreDep);

        Assert.Multiple(() =>
        {
            Assert.That(affected, Is.EqualTo(new[] { "alco-sandbox-quad" }));
            Assert.That(invalidated, Is.EqualTo(new[] { shader }));
            Assert.That(shader.Version, Is.GreaterThan(versionBefore), "version must bump for lazy pipeline rebuild");
            // The shader instance stays alive; its caches were cleared, so the
            // next use recompiles through the (fresh) module system.
            Assert.That(shaderSystem.GetShader("alco-sandbox-quad"), Is.SameAs(shader));
            Assert.DoesNotThrow(() => _ = shader.GetShaderModules());
        });
    }
}
