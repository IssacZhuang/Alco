using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal static partial class UtilsWebGPU
{
    public static uint GetTextureBytesPerRow(PixelFormat pixelFormat, uint width, uint height)
    {
        //uncompresed formats
        if (UtilsPixelFormat.TryGetPixelSize(pixelFormat, out var pixelSize))
        { 
            return width * pixelSize;
        }

        //compressed formats
        //todo: implement compressed formats
        throw new NotImplementedException("Compressed formats are not supported yet");
    }

    public static void PrintHubReport(WGPUHubReport report)
    {
        GraphicsLogger.Info("Hub report:");
        PrintRegistryReport(report.adapters, "adapters");
        PrintRegistryReport(report.devices, "devices");
        PrintRegistryReport(report.queues, "queues");
        PrintRegistryReport(report.pipelineLayouts, "pipelineLayouts");
        PrintRegistryReport(report.shaderModules, "shaderModules");
        PrintRegistryReport(report.bindGroupLayouts, "bindGroupLayouts");
        PrintRegistryReport(report.bindGroups, "bindGroups");
        PrintRegistryReport(report.commandBuffers, "commandBuffers");
        PrintRegistryReport(report.renderBundles, "renderBundles");
        PrintRegistryReport(report.renderPipelines, "renderPipelines");
        PrintRegistryReport(report.computePipelines, "computePipelines");
        PrintRegistryReport(report.querySets, "querySets");
        PrintRegistryReport(report.buffers, "buffers");
        PrintRegistryReport(report.textures, "textures");
        PrintRegistryReport(report.textureViews, "textureViews");
        PrintRegistryReport(report.samplers, "samplers");
    }

    public static void PrintRegistryReport(WGPURegistryReport report, string name)
    {
        GraphicsLogger.Info($"Registry report for {name}:");
        GraphicsLogger.Info($"  Element size: {report.elementSize}");
        GraphicsLogger.Info($"  Allocated: {report.numAllocated}");
        GraphicsLogger.Info($"  Kept from user: {report.numKeptFromUser}");
        GraphicsLogger.Info($"  Released from user: {report.numReleasedFromUser}");
        GraphicsLogger.Info($"  Error: {report.numError}");
    }
}