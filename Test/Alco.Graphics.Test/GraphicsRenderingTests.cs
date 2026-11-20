using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>High-level rendering wrapper tests covering RenderingSystem, RenderContext, RenderBundle, Material, SharedSamplers, and RenderGraph.</summary>
[TestFixture]
[NonParallelizable]
[Category("Rendering")]
public sealed class GraphicsRenderingTests
{
    /// <summary>Runs CPU compilation, culling, aliasing, barrier planning, and zero-allocation checks for RenderGraph.</summary>
    [Test]
    public void RenderGraph_CpuCompilationAndAliasing_PassAllChecks()
    {
        GraphicsCoreTests.RunChecks(RenderGraphTests.RunCpu);
    }

    /// <summary>Runs CPU Slang compilation checks for shared sampler includes.</summary>
    [Test]
    public void SharedSamplers_ShaderCompilation_PassesAllChecks()
    {
        GraphicsCoreTests.RunChecks(SharedSamplerTests.RunCpu);
    }

    /// <summary>Runs RenderingSystem, Material, RenderContext, RenderBundle, and SharedSampler GPU tests on VulkanCompact.</summary>
    [Test]
    public void VulkanCompact_HighLevelRenderingSuite_PassesAllChecks()
    {
        RunRenderingSuiteForBackend(GraphicsBackend.VulkanCompact);
    }

    /// <summary>Runs RenderingSystem, Material, RenderContext, RenderBundle, and SharedSampler GPU tests on the default backend (Auto).</summary>
    [Test]
    public void DefaultBackend_HighLevelRenderingSuite_PassesAllChecks()
    {
        RunRenderingSuiteForBackend(GraphicsBackend.Auto);
    }

    /// <summary>Runs RenderingSystem, Material, RenderContext, RenderBundle, and SharedSampler GPU tests on full Vulkan when supported.</summary>
    [Test]
    public void VulkanFull_HighLevelRenderingSuite_PassesAllChecks()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            Assert.Ignore("Vulkan full backend is only supported on Windows/Linux.");
        RunRenderingSuiteForBackend(GraphicsBackend.Vulkan);
    }

    private static void RunRenderingSuiteForBackend(GraphicsBackend backend)
    {
        GPUDevice? device;
        try
        {
            device = Gpu.CreateDevice(new DeviceDesc { Backend = backend });
        }
        catch (GPUException ex) when (ex.Error == Error.Unsupported)
        {
            Assert.Ignore($"Backend {backend} is unsupported on this host: {ex.Message}");
            return;
        }

        GraphicsCoreTests.RunChecks(check =>
        {
            try
            {
                SharedSamplerTests.RunGpu(device, check);
                RenderingSystemTests.Run(device, check);
                MaterialTests.Run(device, check);
            }
            finally
            {
                device.WaitIdle();
                device.Destroy();
            }
        });
    }
}
