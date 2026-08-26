using NUnit.Framework;
using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine.Test;

public class TestComputeMaterialInstance
{
    // Mirrors the legacy one-set-per-resource layout of the game shader
    // Shaders/Compute/GaussianBlurWithColorGrading.slang (4 sets, 1 resource each),
    // as a self-contained slang module.
    private const string ComputeShaderSource = """
        module test_compute_instance;

        cbuffer input : register(b0, space0)
        {
            [[vk::image_format("rgba16f")]] RWTexture2D<float4> input;
        };

        cbuffer output : register(b0, space1)
        {
            [[vk::image_format("rgba16f")]] RWTexture2D<float4> output;
        };

        cbuffer gaussianKernel : register(b0, space2)
        {
            RWStructuredBuffer<float> gaussianKernel;
        };

        cbuffer data : register(b0, space3) { float4 baseColor; };

        float4 Blur(RWTexture2D<float4> input, RWStructuredBuffer<float> kernel, uint2 id)
        {
            return input[id.xy] + kernel[0];
        }

        [shader("compute")]
        [numthreads(16, 16, 1)]
        void MainCS(uint3 id : SV_DispatchThreadID)
        {
            output[id.xy] = Blur(input, gaussianKernel, id.xy) + baseColor;
        }
        """;

    [Test]
    public void TestComputeInstanceResolvesOwnAndInheritedResources()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        GPUDevice device = renderingSystem.GraphicsDevice;

        Shader shader = renderingSystem.ShaderSystem.GetShaderFromModule(
            "test_compute_instance", "test_compute_instance.slang", ComputeShaderSource);
        ComputeMaterial parent = renderingSystem.CreateComputeMaterial(shader);

        GraphicsValueBuffer<Vector4> dataBuffer = renderingSystem.CreateGraphicsValueBuffer<Vector4>();
        dataBuffer.UpdateBuffer(Vector4.One);
        parent.TrySetBuffer("data", dataBuffer);

        ComputeMaterialInstance instance = parent.CreateInstance();

        GraphicsArrayBuffer<float> kernelBuffer = renderingSystem.CreateGraphicsArrayBuffer<float>(9);
        for (int i = 0; i < 9; i++)
        {
            kernelBuffer[i] = 1f;
        }
        kernelBuffer.UpdateBuffer();
        instance.SetBuffer("gaussianKernel", kernelBuffer);

        RenderTexture input = renderingSystem.CreateRenderTexture(renderingSystem.PreferredLightMapPass, 16, 16);
        RenderTexture output = renderingSystem.CreateRenderTexture(renderingSystem.PreferredLightMapPass, 16, 16);
        instance.TrySetRenderTexture("input", input);
        instance.TrySetRenderTexture("output", output);

        // The dispatch assembles the bind groups: the kernel comes from the
        // instance, _data is inherited from the parent, so every group resolves.
        GPUCommandBuffer command = device.CreateCommandBuffer("test_compute_instance");
        command.Begin();
        using (GPUCommandBuffer.ComputePass computePass = command.BeginCompute())
        {
            instance.DispatchBySize(computePass, 16, 16, 1);
        }
        command.End();
        device.Submit(command);

        for (int i = 0; i < 4; i++)
        {
            Assert.IsNotNull(instance[i], $"resource group {i}");
        }
    }
}
