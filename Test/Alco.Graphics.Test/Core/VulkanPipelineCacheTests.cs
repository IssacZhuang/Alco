// CPU-only Vulkan pipeline-cache persistence tests using the private lifecycle and native dispatch stubs.

using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Vk = Vulkan;

internal static unsafe class VulkanPipelineCacheTests
{
    private const string EnvironmentName = "NOGRAPHICS_VULKAN_PIPELINE_CACHE";
    private const string CacheName = "vulkan-v2-00ab-00cd-0012abcd.bin";
    private const string LegacyName = "vulkan-v1-00ab-00cd-0012abcd.bin";
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type DeviceType = typeof(GPUDevice).Assembly.GetType("Alco.Graphics.LowLevel.VulkanDevice", true)!;
    private static readonly MethodInfo Initialize = DeviceType.BaseType!.GetMethod("InitializePipelineCache", InstanceMembers)!;
    private static readonly MethodInfo Destroy = DeviceType.BaseType!.GetMethod("DestroyPipelineCache", InstanceMembers)!;
    private static readonly byte[] Payload = CreatePayload();
    private static readonly byte[] Captured = new byte[256];
    private static byte[] NativeData = Payload;
    private static int CreateCount, GetCount, DestroyCount, DiagnosticCount;
    private static nuint InitialSize;
    private static bool InitialNull, NativeArgumentsValid, DiagnosticFatal;
    private static string Diagnostic = string.Empty;

    internal static void Run(Action<bool, string> check)
    {
        Console.WriteLine("Vulkan pipeline-cache persistence (CPU-only):");
        string? previousEnvironment = Environment.GetEnvironmentVariable(EnvironmentName);
        FieldInfo callback = typeof(Alco.Graphics.LowLevel.Gpu).GetField("ErrorCallback", BindingFlags.Static | BindingFlags.NonPublic)!;
        ErrorCallback? previousCallback = (ErrorCallback?)callback.GetValue(null);
        string directory = Path.Combine(Path.GetTempPath(), "NoGraphicsSharp-PipelineCache-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            Gpu.SetErrorCallback(OnDiagnostic);
            Environment.SetEnvironmentVariable(EnvironmentName, null);
            Cycle(true);
            check(CreateCount == 1 && InitialSize == 0 && InitialNull && GetCount == 0 && DestroyCount == 1 && NativeArgumentsValid &&
                Directory.GetFileSystemEntries(directory).Length == 0, "unset cache environment creates an empty native cache without persistence");

            Environment.SetEnvironmentVariable(EnvironmentName, Path.Combine(directory, "round-trip"));
            string path = Path.Combine(directory, "round-trip", CacheName);
            Cycle(true);
            check(CreateCount == 1 && InitialSize == 0 && InitialNull && GetCount == 2 && DestroyCount == 1 && NativeArgumentsValid &&
                DiagnosticCount == 0, "missing cache creates once, queries native bytes, and saves on dirty shutdown");
            byte[] saved = File.ReadAllBytes(path);
            check(saved.Length == 48 + Payload.Length && saved.AsSpan(0, 8).SequenceEqual("NGSVKPC2"u8),
                "v2 filename contains a 48-byte envelope with the expected magic");
            check(saved.Length >= 48 && BinaryPrimitives.ReadUInt64LittleEndian(saved.AsSpan(8, 8)) == (ulong)Payload.Length,
                "saved envelope stores the exact payload byte length as little-endian UInt64");
            check(saved.Length >= 48 && saved.AsSpan(16, 32).SequenceEqual(SHA256.HashData(Payload)),
                "saved envelope stores the SHA256 digest of the original native payload");
            check(saved.Length >= 48 && saved.AsSpan(48).SequenceEqual(Payload), "save preserves every raw Vulkan payload byte");
            Cycle(false);
            check(CreateCount == 1 && InitialSize == (nuint)Payload.Length && !InitialNull && Captured.AsSpan(0, Payload.Length).SequenceEqual(Payload),
                "round-trip load passes only the original payload pointer and length to native creation");
            check(GetCount == 0 && DestroyCount == 1 && NativeArgumentsValid && File.ReadAllBytes(path).AsSpan().SequenceEqual(saved),
                "clean loaded cache is destroyed once without querying or rewriting persisted data");

            NativeData = (byte[])Payload.Clone();
            NativeData[^1] ^= 0x5a;
            Cycle(true);
            check(File.ReadAllBytes(path).AsSpan().SequenceEqual(Envelope(NativeData)),
                "dirty save replaces an existing cache with a complete new envelope");
            check(GetCount == 2 && DestroyCount == 1 && NativeArgumentsValid && DiagnosticCount == 0 &&
                Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1,
                $"successful atomic replacement leaves no temporary files (get={GetCount}, destroy={DestroyCount}, native={NativeArgumentsValid}, " +
                $"diagnostics={DiagnosticCount}: {Diagnostic})");
            NativeData = Payload;

            string malformedDirectory = Path.Combine(directory, "malformed");
            Directory.CreateDirectory(malformedDirectory);
            Environment.SetEnvironmentVariable(EnvironmentName, malformedDirectory);
            byte[] valid = Envelope(Payload);
            (string Name, byte[] Data)[] malformed =
            [
                ("empty", []), ("invalid nonempty", new byte[63]), ("old raw Vulkan", Payload),
                ("truncated magic", valid[..7]), ("truncated length", valid[..15]), ("truncated checksum", valid[..47]),
                ("truncated payload", valid[..^1]), ("appended bytes", [.. valid, 0x55]),
                ("bad magic", (byte[])valid.Clone()), ("zero length", (byte[])valid.Clone()),
                ("oversized length", (byte[])valid.Clone()), ("big-endian length", (byte[])valid.Clone()),
                ("bad checksum", (byte[])valid.Clone()), ("bad payload", (byte[])valid.Clone()),
            ];
            foreach ((string name, byte[] data) in malformed)
            {
                switch (name)
                {
                    case "bad magic": data[0] ^= 1; break;
                    case "zero length": BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8, 8), 0); break;
                    case "oversized length": BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8, 8), ulong.MaxValue); break;
                    case "big-endian length": BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(8, 8), (ulong)Payload.Length); break;
                    case "bad checksum": data[16] ^= 1; break;
                    case "bad payload": data[^1] ^= 1; break;
                }
                File.WriteAllBytes(Path.Combine(malformedDirectory, CacheName), data);
                Cycle(false);
                check(CreateCount == 1 && InitialSize == 0 && InitialNull && GetCount == 0 && DestroyCount == 1 && NativeArgumentsValid &&
                    DiagnosticCount == 0, $"{name} cache is rejected before exactly one empty native creation, never a nonempty retry");
            }

            string legacyDirectory = Path.Combine(directory, "legacy");
            Directory.CreateDirectory(legacyDirectory);
            string legacyPath = Path.Combine(legacyDirectory, LegacyName);
            File.WriteAllBytes(legacyPath, Payload);
            Environment.SetEnvironmentVariable(EnvironmentName, legacyDirectory);
            Cycle(true);
            check(CreateCount == 1 && InitialSize == 0 && InitialNull && NativeArgumentsValid, "valid legacy v1 raw cache is ignored by v2 loading");
            check(File.ReadAllBytes(legacyPath).AsSpan().SequenceEqual(Payload) &&
                File.ReadAllBytes(Path.Combine(legacyDirectory, CacheName)).AsSpan().SequenceEqual(valid),
                "v2 saving preserves the legacy v1 file verbatim in its separate namespace");

            string blockedDirectory = Path.Combine(directory, "blocked-save");
            Directory.CreateDirectory(Path.Combine(blockedDirectory, CacheName));
            Environment.SetEnvironmentVariable(EnvironmentName, blockedDirectory);
            Cycle(true);
            check(DiagnosticCount == 1 && !DiagnosticFatal && Diagnostic.StartsWith("Vulkan pipeline cache save:", StringComparison.Ordinal) &&
                GetCount == 2 && DestroyCount == 1 && NativeArgumentsValid, "blocked save reports a nonfatal diagnostic and still destroys the cache");
            check(Directory.Exists(Path.Combine(blockedDirectory, CacheName)) && Directory.GetFiles(blockedDirectory).Length == 0,
                "failed atomic replacement preserves the blocking directory and cleans its temporary file");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvironmentName, previousEnvironment);
            Gpu.SetErrorCallback(previousCallback);
            NativeData = Payload;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void Cycle(bool dirty)
    {
        CreateCount = GetCount = DestroyCount = DiagnosticCount = 0;
        InitialSize = 0;
        InitialNull = DiagnosticFatal = false;
        NativeArgumentsValid = true;
        Diagnostic = string.Empty;
        GPUDevice device = (GPUDevice)Activator.CreateInstance(DeviceType, nonPublic: true)!;
        Vk.VkDeviceApi api = (Vk.VkDeviceApi)RuntimeHelpers.GetUninitializedObject(typeof(Vk.VkDeviceApi));
        typeof(Vk.VkDeviceApi).GetField(nameof(Vk.VkDeviceApi.vkCreatePipelineCache_ptr))!.SetValue(api,
            new Vk.PFN_vkVoidFunction((delegate* unmanaged<void>)(delegate* unmanaged<Vk.VkDevice, Vk.VkPipelineCacheCreateInfo*,
                Vk.VkAllocationCallbacks*, Vk.VkPipelineCache*, Vk.VkResult>)&CreateCache));
        typeof(Vk.VkDeviceApi).GetField(nameof(Vk.VkDeviceApi.vkGetPipelineCacheData_ptr))!.SetValue(api,
            new Vk.PFN_vkVoidFunction((delegate* unmanaged<void>)(delegate* unmanaged<Vk.VkDevice, Vk.VkPipelineCache, nuint*, void*, Vk.VkResult>)&GetData));
        typeof(Vk.VkDeviceApi).GetField(nameof(Vk.VkDeviceApi.vkDestroyPipelineCache_ptr))!.SetValue(api,
            new Vk.PFN_vkVoidFunction((delegate* unmanaged<void>)(delegate* unmanaged<Vk.VkDevice, Vk.VkPipelineCache,
                Vk.VkAllocationCallbacks*, void>)&DestroyCache));
        DeviceType.GetField("Api", InstanceMembers)!.SetValue(device, api);
        Vk.VkPhysicalDeviceProperties properties = new() { vendorID = 0xab, deviceID = 0xcd, driverVersion = 0x12abcd };
        for (int index = 0; index < 16; ++index) properties.pipelineCacheUUID[index] = Payload[16 + index];
        DeviceType.GetField("PhysicalProperties", InstanceMembers)!.SetValue(device, properties);
        try
        {
            Initialize.Invoke(device, null);
            if (dirty) DeviceType.GetField("PipelineCacheDirty", InstanceMembers)!.SetValue(device, true);
        }
        finally
        {
            Destroy.Invoke(device, null);
        }
    }

    private static byte[] CreatePayload()
    {
        byte[] payload = new byte[81];
        for (int index = 16; index < payload.Length; ++index) payload[index] = (byte)(index * 37);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 0xab);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 0xcd);
        return payload;
    }

    private static byte[] Envelope(byte[] payload)
    {
        byte[] contents = new byte[48 + payload.Length];
        "NGSVKPC2"u8.CopyTo(contents);
        BinaryPrimitives.WriteUInt64LittleEndian(contents.AsSpan(8, 8), (ulong)payload.Length);
        SHA256.HashData(payload, contents.AsSpan(16, 32));
        payload.CopyTo(contents, 48);
        return contents;
    }

    private static void OnDiagnostic(string message, bool fatal)
    {
        ++DiagnosticCount;
        Diagnostic = message;
        DiagnosticFatal |= fatal;
    }

    [UnmanagedCallersOnly]
    private static Vk.VkResult CreateCache(Vk.VkDevice device, Vk.VkPipelineCacheCreateInfo* info,
        Vk.VkAllocationCallbacks* allocator, Vk.VkPipelineCache* cache)
    {
        ++CreateCount;
        if (info == null || cache == null) { NativeArgumentsValid = false; return Vk.VkResult.ErrorUnknown; }
        InitialSize = info->initialDataSize;
        InitialNull = info->pInitialData == null;
        NativeArgumentsValid &= info->sType == Vk.VkStructureType.PipelineCacheCreateInfo && allocator == null;
        for (int index = 0; !InitialNull && index < Captured.Length && (nuint)index < InitialSize; ++index)
            Captured[index] = ((byte*)info->pInitialData)[index];
        *cache = new Vk.VkPipelineCache(7);
        return Vk.VkResult.Success;
    }

    [UnmanagedCallersOnly]
    private static Vk.VkResult GetData(Vk.VkDevice device, Vk.VkPipelineCache cache, nuint* size, void* data)
    {
        ++GetCount;
        NativeArgumentsValid &= cache.Handle == 7 && GetCount == (data == null ? 1 : 2);
        if (size == null) { NativeArgumentsValid = false; return Vk.VkResult.ErrorUnknown; }
        if (data != null)
        {
            if (*size < (nuint)NativeData.Length) { NativeArgumentsValid = false; return Vk.VkResult.Incomplete; }
            for (int index = 0; index < NativeData.Length; ++index) ((byte*)data)[index] = NativeData[index];
        }
        *size = (nuint)NativeData.Length;
        return Vk.VkResult.Success;
    }

    [UnmanagedCallersOnly]
    private static void DestroyCache(Vk.VkDevice device, Vk.VkPipelineCache cache, Vk.VkAllocationCallbacks* allocator)
    {
        ++DestroyCount;
        NativeArgumentsValid &= cache.Handle == 7 && allocator == null;
    }
}
