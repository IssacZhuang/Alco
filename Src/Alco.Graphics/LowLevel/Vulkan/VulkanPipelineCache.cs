// Vulkan pipeline cache with opt-in on-disk persistence across runs.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Vk = Vulkan;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal abstract unsafe partial class VulkanDeviceBase
{
    private const int PipelineCacheHeaderSize = 16 + SHA256.HashSizeInBytes;
    private string? PipelineCachePath;
    internal Vk.VkPipelineCache PipelineCache;
    internal bool PipelineCacheDirty;

    private void InitializePipelineCache()
    {
        string? directory = Environment.GetEnvironmentVariable("NOGRAPHICS_VULKAN_PIPELINE_CACHE");
        byte[]? initial = null;
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                PipelineCachePath = Path.Combine(Path.GetFullPath(directory),
                    $"vulkan-v2-{PhysicalProperties.vendorID:x4}-{PhysicalProperties.deviceID:x4}-{PhysicalProperties.driverVersion:x8}.bin");
                if (File.Exists(PipelineCachePath))
                {
                    byte[] contents = File.ReadAllBytes(PipelineCachePath);
                    if (contents.Length >= PipelineCacheHeaderSize && contents.AsSpan(0, 8).SequenceEqual("NGSVKPC2"u8) &&
                        BinaryPrimitives.ReadUInt64LittleEndian(contents.AsSpan(8, sizeof(ulong))) == (ulong)(contents.Length - PipelineCacheHeaderSize))
                    {
                        Span<byte> checksum = stackalloc byte[SHA256.HashSizeInBytes];
                        SHA256.HashData(contents.AsSpan(PipelineCacheHeaderSize), checksum);
                        if (contents.AsSpan(16, checksum.Length).SequenceEqual(checksum)) initial = contents;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Gpu.ReportError($"Vulkan pipeline cache load: {exception.Message}", false);
                PipelineCachePath = null;
            }
        }

        fixed (byte* data = initial)
        {
            Vk.VkPipelineCacheCreateInfo cacheInfo = new()
            {
                initialDataSize = initial == null ? 0 : (nuint)(initial.Length - PipelineCacheHeaderSize),
                pInitialData = initial == null ? null : data + PipelineCacheHeaderSize,
            };
            if (initial == null ||
                Backend.ErrorFromVk(Api.vkCreatePipelineCache(&cacheInfo, null, out PipelineCache), "vkCreatePipelineCache") != Error.None)
            {
                cacheInfo = new();
                if (Backend.ErrorFromVk(Api.vkCreatePipelineCache(&cacheInfo, null, out PipelineCache), "vkCreatePipelineCache") != Error.None)
                    PipelineCache = default;
            }
        }
    }

    private void DestroyPipelineCache()
    {
        if (PipelineCache.IsNull) return;
        if (PipelineCachePath != null && Volatile.Read(ref PipelineCacheDirty))
        {
            string? temporary = null;
            try
            {
                nuint size = 0;
                if (Backend.ErrorFromVk(Api.vkGetPipelineCacheData(PipelineCache, &size, null), "vkGetPipelineCacheData") == Error.None && size != 0)
                {
                    void* data = NativeMemory.Alloc(size);
                    try
                    {
                        if (Backend.ErrorFromVk(Api.vkGetPipelineCacheData(PipelineCache, &size, data), "vkGetPipelineCacheData") == Error.None)
                        {
                            string? directory = Path.GetDirectoryName(PipelineCachePath);
                            if (directory != null) Directory.CreateDirectory(directory);
                            temporary = PipelineCachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            ReadOnlySpan<byte> contents = new(data, (int)size);
                            Span<byte> header = stackalloc byte[PipelineCacheHeaderSize];
                            "NGSVKPC2"u8.CopyTo(header);
                            BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(8, sizeof(ulong)), (ulong)size);
                            SHA256.HashData(contents, header[16..]);
                            using (FileStream file = File.Create(temporary))
                            {
                                file.Write(header);
                                file.Write(contents);
                            }
                            File.Move(temporary, PipelineCachePath, true);
                            temporary = null;
                        }
                    }
                    finally
                    {
                        NativeMemory.Free(data);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Gpu.ReportError($"Vulkan pipeline cache save: {exception.Message}", false);
            }
            finally
            {
                if (temporary != null)
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        Gpu.ReportError($"Vulkan pipeline cache cleanup: {exception.Message}", false);
                    }
            }
        }
        Api.vkDestroyPipelineCache(PipelineCache, null);
        PipelineCache = default;
    }
}
