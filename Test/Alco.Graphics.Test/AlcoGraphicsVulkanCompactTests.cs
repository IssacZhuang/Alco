using System.Numerics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>End-to-end GPU tests targeting the VulkanCompact backend.</summary>
[TestFixture]
[NonParallelizable]
[Category("VulkanCompact")]
public sealed class AlcoGraphicsVulkanCompactTests
{
    private const string ComputeWriteShaderSource = """
        import Core;

        [GPURoot]
        struct ComputeRoot
        {
            GPUBufferView Output;
            float4 Value;
        };

        [shader("compute")]
        [numthreads(1, 1, 1)]
        void csMain(uint3 id : SV_DispatchThreadID)
        {
            ComputeRoot root = gpu_load_root<ComputeRoot>();
            gpu_store(root.Output, id.x, root.Value * float(id.x + 1));
        }
        """;

    private const string QuadShaderSource = """
        import Core;

        [GPURoot]
        struct QuadRoot
        {
            GPUTextureId Albedo;
            GPUSamplerId Sampler;
            float4 Tint;
        };

        struct VsOut
        {
            float4 Pos : SV_Position;
            float2 Uv : TEXCOORD0;
        };

        [shader("vertex")]
        VsOut vsMain(uint vertexId : SV_VertexID)
        {
            float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
            VsOut o;
            o.Pos = float4(uv * 2.0 - 1.0, 0.0, 1.0);
            o.Uv = uv;
            return o;
        }

        [shader("pixel")]
        float4 psMain(VsOut input) : SV_Target
        {
            QuadRoot root = gpu_load_root<QuadRoot>();
            float4 sampled = gpu_texture<Texture2D>(root.Albedo.index).Sample(gpu_sampler(root.Sampler.index), input.Uv);
            return sampled * root.Tint;
        }
        """;

    private static GPUDevice CreateVulkanCompactOrIgnore()
    {
        try
        {
            return Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.VulkanCompact });
        }
        catch (GPUException ex)
        {
            Assert.Ignore($"VulkanCompact backend unavailable on this host: {ex.Message}");
            throw;
        }
    }

    /// <summary>Executes a compute shader on VulkanCompact and verifies buffer readback values.</summary>
    [Test]
    public unsafe void VulkanCompact_ComputeDispatch_WritesExpectedOutput()
    {
        GPUDevice device = CreateVulkanCompactOrIgnore();
        RenderingSystem system = new(device, textureHeapByteCount: 2 * 1024 * 1024, bufferHeapByteCount: 2 * 1024 * 1024);
        RenderContext context = new(system);
        try
        {
            Shader shader = system.Shaders.GetShaderFromSource("vkc_compute_write", "vkc_compute_write.slang", ComputeWriteShaderSource);
            Buffer output = system.CreateBuffer(4 * (ulong)sizeof(Vector4));
            Material material = shader.CreateMaterial();
            material.SetBuffer("Output", output);
            Vector4 val = new(1.0f, 2.0f, 3.0f, 4.0f);
            material.SetData("Value", ref val);

            context.BeginFrame();
            using (var compute = context.BeginCompute())
            {
                compute.Dispatch(material, new Extent3D(4, 1, 1));
            }
            context.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Host, Access.HostRead);
            context.SubmitWithCompletion().Wait();

            GPUHeap readback = device.AllocateGPUHeap(4 * (ulong)sizeof(Vector4), MemoryType.Readback);
            try
            {
                context.BeginFrame();
                context.CopyMemory(output.GetRange(), readback.Range.GPURange);
                context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                context.SubmitWithCompletion().Wait();

                Vector4* results = (Vector4*)readback.Range.Cpu;
                Assert.That(results[0], Is.EqualTo(new Vector4(1, 2, 3, 4)));
                Assert.That(results[1], Is.EqualTo(new Vector4(2, 4, 6, 8)));
                Assert.That(results[2], Is.EqualTo(new Vector4(3, 6, 9, 12)));
                Assert.That(results[3], Is.EqualTo(new Vector4(4, 8, 12, 16)));
            }
            finally
            {
                readback.Destroy();
                output.Dispose();
            }
        }
        finally
        {
            context.Dispose();
            system.Dispose();
            device.Destroy();
        }
    }

    /// <summary>Executes a bindless graphics pass on VulkanCompact and verifies rendered pixel readback.</summary>
    [Test]
    public unsafe void VulkanCompact_GraphicsBindlessQuad_RendersExpectedColor()
    {
        GPUDevice device = CreateVulkanCompactOrIgnore();
        RenderingSystem system = new(device, textureHeapByteCount: 4 * 1024 * 1024, bufferHeapByteCount: 2 * 1024 * 1024);
        RenderContext context = new(system);
        try
        {
            Shader shader = system.Shaders.GetShaderFromSource("vkc_bindless_quad", "vkc_bindless_quad.slang", QuadShaderSource);
            Texture target = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(4, 4, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            });
            Material material = shader.CreateMaterial();
            material.SetTexture("Albedo", system.TextureWhite);
            material.SetSampler("Sampler", system.Samplers.NearestClamp);
            Vector4 tint = new(1.0f, 0.5f, 0.25f, 1.0f);
            material.SetData("Tint", ref tint);

            GPUHeap readback = device.AllocateGPUHeap(4 * 4 * 4, MemoryType.Readback);
            try
            {
                context.BeginFrame();
                RenderingDesc passDesc = new()
                {
                    Colors = [new ColorAttachment { Target = target, Load = LoadOp.Clear, Store = StoreOp.Store }],
                };
                using (var render = context.BeginRenderPass(passDesc))
                {
                    render.SetViewport(new Viewport { Width = 4, Height = 4, MaxDepth = 1 });
                    render.SetScissor(new Scissor { Width = 4, Height = 4 });
                    render.Draw(material, 3);
                }
                context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                context.CopyTextureToMemory(target.GetNativeTexture(), readback.Range.GPURange);
                context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                context.SubmitWithCompletion().Wait();

                byte* pixels = readback.Range.Cpu;
                Assert.That(pixels[0], Is.EqualTo(255));
                Assert.That(pixels[1], Is.InRange((byte)127, (byte)128));
                Assert.That(pixels[2], Is.InRange((byte)63, (byte)64));
                Assert.That(pixels[3], Is.EqualTo(255));
            }
            finally
            {
                readback.Destroy();
                target.Dispose();
            }
        }
        finally
        {
            context.Dispose();
            system.Dispose();
            device.Destroy();
        }
    }
}
