// Device-local compiled shader libraries and opt-in Metal 4 pipeline archive persistence.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Mtl = Metal;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal unsafe struct MetalLibraryEntry
{
    internal byte* Code;
    internal int Size;
    internal int Hash;
    internal Mtl.MTLLibrary Library;
}

internal sealed unsafe partial class MetalDevice
{
    private const int ShaderLibraryCapacity = 64;
    private const int ShaderLibraryByteCapacity = 8 * 1024 * 1024;
    private readonly Lock ShaderLibraryLock = new();
    private readonly MetalLibraryEntry[] ShaderLibraries = new MetalLibraryEntry[ShaderLibraryCapacity];
    private int ShaderLibraryBytes;
    private int NextShaderLibrary;
    private string? PipelineCachePath;
    private Mtl.MTL4PipelineDataSetSerializer PipelineSerializer;
    private Mtl.MTL4Archive PipelineArchive;
    private Mtl.MTL4CompilerTaskOptions PipelineTaskOptions;
    private bool PipelineCacheDirty;

    private void InitializePipelineCache(Mtl.MTL4CompilerDescriptor compiler)
    {
        string? directory = Environment.GetEnvironmentVariable("ALCO_GRAPHICS_METAL_PIPELINE_CACHE");
        if (string.IsNullOrEmpty(directory)) return;
        try
        {
            string? system = MetalInterop.SystemBuild();
            if (system == null)
            {
                Gpu.ReportError("Metal pipeline cache: the macOS build could not be read.", false);
                return;
            }
            string model = Convert.ToHexString(Encoding.UTF8.GetBytes($"{MtlDevice.Name}"));
            PipelineCachePath = Path.Combine(Path.GetFullPath(directory), $"metal4-v2-{MtlDevice.RegistryID:x16}-{model}-{system}.metallib");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Gpu.ReportError($"Metal pipeline cache path: {exception.Message}", false);
            return;
        }

        using Mtl.MTL4PipelineDataSetSerializerDescriptor descriptor = new()
        {
            Configuration = Mtl.MTL4PipelineDataSetSerializerConfiguration.CaptureBinaries,
        };
        Mtl.NSError error = default;
        PipelineSerializer = MtlDevice.NewPipelineDataSetSerializer(descriptor);
        if (PipelineSerializer.NativePtr == 0)
        {
            MetalInterop.ReportError(error, "pipeline cache serializer");
            PipelineCachePath = null;
            return;
        }
        compiler.PipelineDataSetSerializer = PipelineSerializer;
        if (!File.Exists(PipelineCachePath)) return;
        try
        {
            // Metal's archive loader can dereference malformed data before returning an NSError.
            Span<byte> expected = stackalloc byte[SHA256.HashSizeInBytes];
            using (FileStream checksum = File.OpenRead(PipelineCachePath + ".sha256"))
            {
                if (checksum.Length != expected.Length)
                {
                    Gpu.ReportError("Metal pipeline cache: invalid archive checksum.", false);
                    return;
                }
                checksum.ReadExactly(expected);
            }
            Span<byte> actual = stackalloc byte[SHA256.HashSizeInBytes];
            using (FileStream archive = File.OpenRead(PipelineCachePath)) SHA256.HashData(archive, actual);
            if (!actual.SequenceEqual(expected))
            {
                Gpu.ReportError("Metal pipeline cache: archive checksum mismatch.", false);
                return;
            }
            Mtl.NSString path = PipelineCachePath;
            Mtl.NSURL url = Mtl.NSURL.FileURLWithPath(path);
            error = default;
            PipelineArchive = MtlDevice.NewArchive(url, ref error);
            if (PipelineArchive.NativePtr == 0)
            {
                MetalInterop.ReportError(error, "pipeline cache load");
                return;
            }
            PipelineTaskOptions = new Mtl.MTL4CompilerTaskOptions();
            PipelineTaskOptions.LookupArchives = Mtl.NSArray.Array(new Mtl.NSObject(PipelineArchive.NativePtr));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Gpu.ReportError($"Metal pipeline cache load: {exception.Message}", false);
        }
    }

    private void DestroyShaderCache()
    {
        if (PipelineCachePath != null && PipelineCacheDirty)
        {
            string? temporary = null;
            try
            {
                string? directory = Path.GetDirectoryName(PipelineCachePath);
                if (directory != null) Directory.CreateDirectory(directory);
                temporary = PipelineCachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                Mtl.NSString path = temporary;
                Mtl.NSURL url = Mtl.NSURL.FileURLWithPath(path);
                Mtl.NSError error = default;
                if (PipelineSerializer.SerializeAsArchiveAndFlushToURL(url, ref error))
                {
                    Span<byte> checksum = stackalloc byte[SHA256.HashSizeInBytes];
                    using (FileStream archive = File.OpenRead(temporary)) SHA256.HashData(archive, checksum);
                    File.WriteAllBytes(temporary + ".sha256", checksum);
                    File.Move(temporary, PipelineCachePath, true);
                    File.Move(temporary + ".sha256", PipelineCachePath + ".sha256", true);
                }
                else
                    MetalInterop.ReportError(error, "pipeline cache save");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Gpu.ReportError($"Metal pipeline cache save: {exception.Message}", false);
            }
            finally
            {
                if (temporary != null)
                    for (int i = 0; i < 2; i++)
                        try
                        {
                            File.Delete(i == 0 ? temporary : temporary + ".sha256");
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            Gpu.ReportError($"Metal pipeline cache cleanup: {exception.Message}", false);
                        }
            }
        }
        PipelineTaskOptions.Dispose();
        PipelineTaskOptions = default;
        PipelineArchive.Dispose();
        PipelineArchive = default;
        PipelineSerializer.Dispose();
        PipelineSerializer = default;
        for (int i = 0; i < ShaderLibraries.Length; i++) EvictShaderLibrary(i);
    }

    private Mtl.MTLLibrary ShaderLibrary(ReadOnlySpan<byte> code, string entryPoint)
    {
        if (code.Length > ShaderLibraryByteCapacity) return CompileShaderLibrary(code, entryPoint);
        HashCode hash = new();
        hash.AddBytes(code);
        int key = hash.ToHashCode();
        lock (ShaderLibraryLock)
        {
            Mtl.MTLLibrary cached = FindShaderLibrary(code, key);
            if (cached.NativePtr != 0) return cached;
        }

        Mtl.MTLLibrary library = CompileShaderLibrary(code, entryPoint);
        lock (ShaderLibraryLock)
        {
            Mtl.MTLLibrary cached = FindShaderLibrary(code, key);
            if (cached.NativePtr != 0)
            {
                library.Dispose();
                return cached;
            }
            EvictShaderLibrary(NextShaderLibrary);
            while (ShaderLibraryBytes + code.Length > ShaderLibraryByteCapacity)
            {
                NextShaderLibrary = (NextShaderLibrary + 1) % ShaderLibraryCapacity;
                EvictShaderLibrary(NextShaderLibrary);
            }
            byte* copy = (byte*)NativeMemory.Alloc((nuint)code.Length);
            code.CopyTo(new Span<byte>(copy, code.Length));
            ShaderLibraries[NextShaderLibrary] = new MetalLibraryEntry
            {
                Code = copy,
                Size = code.Length,
                Hash = key,
                Library = new Mtl.MTLLibrary(MetalInterop.retain(library.NativePtr)),
            };
            ShaderLibraryBytes += code.Length;
            NextShaderLibrary = (NextShaderLibrary + 1) % ShaderLibraryCapacity;
        }
        return library;
    }

    private Mtl.MTLLibrary FindShaderLibrary(ReadOnlySpan<byte> code, int hash)
    {
        foreach (MetalLibraryEntry entry in ShaderLibraries)
            if (entry.Hash == hash && entry.Size == code.Length && new ReadOnlySpan<byte>(entry.Code, entry.Size).SequenceEqual(code))
                return new Mtl.MTLLibrary(MetalInterop.retain(entry.Library.NativePtr));
        return default;
    }

    private void EvictShaderLibrary(int index)
    {
        ref MetalLibraryEntry entry = ref ShaderLibraries[index];
        entry.Library.Dispose();
        NativeMemory.Free(entry.Code);
        ShaderLibraryBytes -= entry.Size;
        entry = default;
    }

    private Mtl.MTLLibrary CompileShaderLibrary(ReadOnlySpan<byte> code, string entryPoint)
    {
        if (code.Length >= 4 && code[0] == (byte)'M' && code[1] == (byte)'T' && code[2] == (byte)'L' && code[3] == (byte)'B')
        {
            fixed (byte* bytes = code)
            {
                nint data = MetalInterop.DispatchDataCreate(bytes, (nuint)code.Length, 0, 0);
                if (data == 0) throw MetalBackend.CreationFailure(default, "shader library data");
                try
                {
                    Mtl.NSError error = default;
                    Mtl.MTLLibrary library = MtlDevice.NewLibrary(data, ref error);
                    if (library.NativePtr == 0) throw MetalBackend.CreationFailure(error, $"shader library for {entryPoint}");
                    return library;
                }
                finally
                {
                    MetalInterop.DispatchRelease(data);
                }
            }
        }
        using Mtl.MTLCompileOptions options = new() { LanguageVersion = Mtl.MTLLanguageVersion.Version40 };
        using Mtl.MTL4LibraryDescriptor descriptor = new() { Source = Encoding.UTF8.GetString(code), Options = options };
        Mtl.NSError sourceError = default;
        Mtl.MTLLibrary sourceLibrary = MtlCompiler.NewLibrary(descriptor, ref sourceError);
        if (sourceLibrary.NativePtr == 0) throw MetalBackend.CreationFailure(sourceError, $"shader source for {entryPoint}");
        return sourceLibrary;
    }
}
