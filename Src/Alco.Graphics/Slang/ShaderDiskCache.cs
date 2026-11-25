// Single-file atomic L1 module IR + metadata cache (.agmod) and L2 compiled program cache (.agshader).

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Alco.Graphics.Slang;

internal sealed class CachedModuleMetadata
{
    public required string ModuleName { get; init; }
    public string? FilePath { get; init; }
    public required ShaderModuleEntryPoint[] EntryPoints { get; init; }
    public required string[] DependencyPaths { get; init; }
    public required string[] ImportedModules { get; init; }
}

internal sealed class ShaderDiskCache
{
    private const uint ProgramMagic = 0x43504741u; // "AGPC"
    private const ushort ProgramFormatVersion = 1;
    private const int ProgramHeaderSize = 40; // magic(4) + version(2) + target(1) + reserved(1) + payloadSha256(32)

    private const uint ModuleMagic = 0x444D4741u; // "AGMD"
    private const ushort ModuleFormatVersion = 1;
    private const byte ModuleFlagHasIr = 1 << 0;
    private const int ModuleHeaderSize = 44; // magic(4) + version(2) + target(1) + flags(1) + metaLen(4) + metaSha256(32)
    private const int IrSubheaderSize = 4 + SHA256.HashSizeInBytes;

    private readonly string _modulesDirectory;
    private readonly string _programsDirectory;
    private readonly ShaderTarget _target;
    private readonly string _compilerVersionTag;
    private readonly Action<string>? _log;

    internal ShaderDiskCache(string cacheDirectory, ShaderTarget target, string compilerVersionTag, Action<string>? log)
    {
        _modulesDirectory = Path.Combine(cacheDirectory, "modules");
        _programsDirectory = Path.Combine(cacheDirectory, "programs");
        _target = target;
        _compilerVersionTag = compilerVersionTag;
        _log = log;
    }

    internal bool TryReadProgram(string key, Func<string, byte[]?> resolveSourceHash, [NotNullWhen(true)] out CompiledShader? shader)
    {
        shader = null;
        string path = GetProgramPath(key);
        if (!File.Exists(path))
            return false;

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < ProgramHeaderSize)
                return false;

            ReadOnlySpan<byte> header = bytes.AsSpan(0, ProgramHeaderSize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != ProgramMagic ||
                BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2)) != ProgramFormatVersion ||
                (ShaderTarget)header[6] != _target)
            {
                return false;
            }

            ReadOnlySpan<byte> payload = bytes.AsSpan(ProgramHeaderSize);
            Span<byte> actualSha = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(payload, actualSha);
            if (!actualSha.SequenceEqual(header.Slice(8, SHA256.HashSizeInBytes)))
            {
                _log?.Invoke($"Shader cache corrupt (checksum mismatch): '{key[..Math.Min(12, key.Length)]}'.");
                return false;
            }

            using MemoryStream stream = new(bytes, ProgramHeaderSize, bytes.Length - ProgramHeaderSize, writable: false);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

            if (!string.Equals(reader.ReadString(), _compilerVersionTag, StringComparison.Ordinal))
                return false;

            int depCount = reader.ReadInt32();
            if (depCount < 0)
                return false;

            for (int i = 0; i < depCount; i++)
            {
                string depPath = reader.ReadString();
                ReadOnlySpan<byte> expectedSha = ReadExactBytes(reader, SHA256.HashSizeInBytes);
                byte[]? currentSha = resolveSourceHash(depPath);
                if (currentSha == null || !currentSha.AsSpan().SequenceEqual(expectedSha))
                    return false;
            }

            int artifactLength = reader.ReadInt32();
            int artifactOffset = ProgramHeaderSize + (int)stream.Position;
            if (artifactLength <= 0 || artifactOffset + artifactLength != bytes.Length)
                return false;

            shader = CompiledShader.Deserialize(bytes.AsSpan(artifactOffset, artifactLength));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
            _log?.Invoke($"Shader cache miss/invalid ({key[..Math.Min(12, key.Length)]}): {exception.Message}");
            shader = null;
            return false;
        }
    }

    internal void WriteProgram(string key, IReadOnlyList<(string Path, byte[] Sha256)> dependencies, CompiledShader shader)
    {
        try
        {
            using MemoryStream stream = new();
            stream.SetLength(ProgramHeaderSize);
            stream.Position = ProgramHeaderSize;

            using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(_compilerVersionTag);
                writer.Write(dependencies.Count);
                for (int i = 0; i < dependencies.Count; i++)
                {
                    writer.Write(dependencies[i].Path);
                    writer.Write(dependencies[i].Sha256);
                }

                byte[] artifact = shader.Serialize();
                writer.Write(artifact.Length);
                writer.Write(artifact);
                writer.Flush();
            }

            if (!stream.TryGetBuffer(out ArraySegment<byte> segment))
            {
                byte[] copy = stream.ToArray();
                segment = new ArraySegment<byte>(copy);
            }

            Span<byte> span = segment.AsSpan();
            BinaryPrimitives.WriteUInt32LittleEndian(span, ProgramMagic);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), ProgramFormatVersion);
            span[6] = (byte)_target;
            span[7] = 0;
            SHA256.HashData(span[ProgramHeaderSize..], span.Slice(8, SHA256.HashSizeInBytes));

            WriteBytesAtomic(_programsDirectory, GetProgramPath(key), span);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _log?.Invoke($"Shader cache write skipped ({key[..Math.Min(12, key.Length)]}): {exception.Message}");
        }
    }

    internal bool TryReadModuleMetadata(
        string key,
        Func<string, byte[]?> resolveSourceHash,
        [NotNullWhen(true)] out CachedModuleMetadata? metadata)
    {
        metadata = null;
        string path = GetModulePath(key);
        if (!File.Exists(path))
            return false;

        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return TryReadValidatedMetadata(stream, requireIr: false, resolveSourceHash, out metadata, out _);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
            _log?.Invoke($"Shader IR metadata miss/invalid ({key[..Math.Min(12, key.Length)]}): {exception.Message}");
            metadata = null;
            return false;
        }
    }

    internal bool TryReadModuleIr(
        string key,
        Func<string, byte[]?> resolveSourceHash,
        [NotNullWhen(true)] out CachedModuleMetadata? metadata,
        [NotNullWhen(true)] out byte[]? irBytes)
    {
        metadata = null;
        irBytes = null;
        string path = GetModulePath(key);
        if (!File.Exists(path))
            return false;

        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!TryReadValidatedMetadata(stream, requireIr: true, resolveSourceHash, out metadata, out _))
                return false;

            if (stream.Length - stream.Position < IrSubheaderSize)
                return false;

            Span<byte> irHeader = stackalloc byte[IrSubheaderSize];
            stream.ReadExactly(irHeader);
            int irLength = BinaryPrimitives.ReadInt32LittleEndian(irHeader);
            if (irLength <= 0 || stream.Length - stream.Position != irLength)
            {
                _log?.Invoke($"Shader IR cache corrupt (truncated IR): '{key[..Math.Min(12, key.Length)]}'.");
                metadata = null;
                return false;
            }

            byte[] ir = new byte[irLength];
            stream.ReadExactly(ir);

            Span<byte> actualIrSha = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(ir, actualIrSha);
            if (!actualIrSha.SequenceEqual(irHeader.Slice(4, SHA256.HashSizeInBytes)))
            {
                _log?.Invoke($"Shader IR cache corrupt (IR checksum mismatch): '{key[..Math.Min(12, key.Length)]}'.");
                metadata = null;
                return false;
            }

            irBytes = ir;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
            _log?.Invoke($"Shader IR cache miss/invalid ({key[..Math.Min(12, key.Length)]}): {exception.Message}");
            metadata = null;
            irBytes = null;
            return false;
        }
    }

    internal void WriteModule(
        string key,
        CachedModuleMetadata metadata,
        IReadOnlyList<(string Path, byte[] Sha256)> dependencies,
        byte[] irBytes)
    {
        if (irBytes.Length == 0)
            return;

        try
        {
            using MemoryStream stream = new();
            stream.SetLength(ModuleHeaderSize);
            stream.Position = ModuleHeaderSize;

            uint metaLength;
            using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(_compilerVersionTag);
                writer.Write(metadata.ModuleName);
                writer.Write(metadata.FilePath != null);
                if (metadata.FilePath != null)
                    writer.Write(metadata.FilePath);

                writer.Write(metadata.EntryPoints.Length);
                for (int i = 0; i < metadata.EntryPoints.Length; i++)
                {
                    ShaderModuleEntryPoint ep = metadata.EntryPoints[i];
                    writer.Write(ep.Name);
                    writer.Write((uint)ep.Stage);
                    writer.Write(ep.HasGenericParameters);
                    writer.Write(ep.GenericParameters.Count);
                    for (int g = 0; g < ep.GenericParameters.Count; g++)
                    {
                        ShaderGenericParam gp = ep.GenericParameters[g];
                        writer.Write(gp.Name);
                        writer.Write(gp.IsTypeParameter);
                        writer.Write(gp.ConstraintTypeName != null);
                        if (gp.ConstraintTypeName != null)
                            writer.Write(gp.ConstraintTypeName);
                        writer.Write((byte)gp.ValueScalarType);
                    }
                }

                writer.Write(dependencies.Count);
                for (int i = 0; i < dependencies.Count; i++)
                {
                    writer.Write(dependencies[i].Path);
                    writer.Write(dependencies[i].Sha256);
                }

                writer.Write(metadata.ImportedModules.Length);
                for (int i = 0; i < metadata.ImportedModules.Length; i++)
                    writer.Write(metadata.ImportedModules[i]);

                writer.Flush();
                metaLength = checked((uint)(stream.Position - ModuleHeaderSize));

                Span<byte> irSha = stackalloc byte[SHA256.HashSizeInBytes];
                SHA256.HashData(irBytes, irSha);
                writer.Write(irBytes.Length);
                writer.Write(irSha);
                writer.Write(irBytes);
                writer.Flush();
            }

            if (!stream.TryGetBuffer(out ArraySegment<byte> segment))
            {
                byte[] copy = stream.ToArray();
                segment = new ArraySegment<byte>(copy);
            }

            Span<byte> span = segment.AsSpan();
            BinaryPrimitives.WriteUInt32LittleEndian(span, ModuleMagic);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), ModuleFormatVersion);
            span[6] = (byte)_target;
            span[7] = ModuleFlagHasIr;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8, 4), metaLength);
            SHA256.HashData(span.Slice(ModuleHeaderSize, (int)metaLength), span.Slice(12, SHA256.HashSizeInBytes));

            WriteBytesAtomic(_modulesDirectory, GetModulePath(key), span);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _log?.Invoke($"Shader IR cache write skipped ({metadata.ModuleName}): {exception.Message}");
        }
    }

    private bool TryReadValidatedMetadata(
        FileStream stream,
        bool requireIr,
        Func<string, byte[]?> resolveSourceHash,
        [NotNullWhen(true)] out CachedModuleMetadata? metadata,
        out byte flags)
    {
        metadata = null;
        flags = 0;
        if (stream.Length < ModuleHeaderSize)
            return false;

        Span<byte> header = stackalloc byte[ModuleHeaderSize];
        stream.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != ModuleMagic ||
            BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2)) != ModuleFormatVersion ||
            (ShaderTarget)header[6] != _target)
        {
            return false;
        }

        flags = header[7];
        if (requireIr && (flags & ModuleFlagHasIr) == 0)
            return false;

        uint metaLength = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, 4));
        if (metaLength == 0 || metaLength > int.MaxValue || stream.Length - ModuleHeaderSize < metaLength)
            return false;

        byte[] metaBytes = new byte[(int)metaLength];
        stream.ReadExactly(metaBytes);

        Span<byte> actualMetaSha = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(metaBytes, actualMetaSha);
        if (!actualMetaSha.SequenceEqual(header.Slice(12, SHA256.HashSizeInBytes)))
            return false;

        using MemoryStream metaStream = new(metaBytes, writable: false);
        using BinaryReader reader = new(metaStream, Encoding.UTF8, leaveOpen: true);

        if (!string.Equals(reader.ReadString(), _compilerVersionTag, StringComparison.Ordinal))
            return false;

        string moduleName = reader.ReadString();
        string? filePath = reader.ReadBoolean() ? reader.ReadString() : null;

        int epCount = reader.ReadInt32();
        if (epCount < 0)
            return false;

        ShaderModuleEntryPoint[] entryPoints = epCount == 0 ? [] : new ShaderModuleEntryPoint[epCount];
        for (int i = 0; i < epCount; i++)
        {
            string epName = reader.ReadString();
            ShaderStageKind stage = (ShaderStageKind)reader.ReadUInt32();
            bool hasGenerics = reader.ReadBoolean();
            int gpCount = reader.ReadInt32();
            if (gpCount < 0)
                return false;

            ShaderGenericParam[] generics = gpCount == 0 ? [] : new ShaderGenericParam[gpCount];
            for (int g = 0; g < gpCount; g++)
            {
                string gpName = reader.ReadString();
                bool isType = reader.ReadBoolean();
                string? constraint = reader.ReadBoolean() ? reader.ReadString() : null;
                ShaderScalarType scalarType = (ShaderScalarType)reader.ReadByte();
                generics[g] = new ShaderGenericParam
                {
                    Name = gpName,
                    IsTypeParameter = isType,
                    ConstraintTypeName = constraint,
                    ValueScalarType = scalarType,
                };
            }

            entryPoints[i] = new ShaderModuleEntryPoint(epName, stage, hasGenerics)
            {
                GenericParameters = generics,
            };
        }

        int depCount = reader.ReadInt32();
        if (depCount < 0)
            return false;

        string[] depPaths = depCount == 0 ? [] : new string[depCount];
        for (int i = 0; i < depCount; i++)
        {
            string depPath = reader.ReadString();
            ReadOnlySpan<byte> expectedSha = ReadExactBytes(reader, SHA256.HashSizeInBytes);
            byte[]? currentSha = resolveSourceHash(depPath);
            if (currentSha == null || !currentSha.AsSpan().SequenceEqual(expectedSha))
                return false;
            depPaths[i] = depPath;
        }

        int importedCount = reader.ReadInt32();
        if (importedCount < 0)
            return false;

        string[] imported = importedCount == 0 ? [] : new string[importedCount];
        for (int i = 0; i < importedCount; i++)
            imported[i] = reader.ReadString();

        if (metaStream.Position != metaBytes.Length)
            return false;

        metadata = new CachedModuleMetadata
        {
            ModuleName = moduleName,
            FilePath = filePath,
            EntryPoints = entryPoints,
            DependencyPaths = depPaths,
            ImportedModules = imported,
        };
        return true;
    }

    private static ReadOnlySpan<byte> ReadExactBytes(BinaryReader reader, int count)
    {
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
            throw new EndOfStreamException("Unexpected end of cached shader metadata.");
        return bytes;
    }

    private static void WriteBytesAtomic(string directory, string destinationPath, ReadOnlySpan<byte> data)
    {
        Directory.CreateDirectory(directory);
        string tempPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream file = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(data);
            }
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private string GetModulePath(string key) => Path.Combine(_modulesDirectory, key + ".agmod");

    private string GetProgramPath(string key) => Path.Combine(_programsDirectory, key + ".agshader");
}
