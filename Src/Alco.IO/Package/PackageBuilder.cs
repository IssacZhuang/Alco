using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

using Alco;

namespace Alco.IO;

/// <summary>
/// Builds an Alco package in-memory following the documented format:
/// [magic][Int64 LE meta length][meta payload via BinaryParser][content payload].
/// The meta type <typeparamref name="TMeta"/> supplies the magic number and may carry type-specific
/// fields beyond the inherited entry directory.
/// </summary>
/// <typeparam name="TMeta">The package metadata type, which must implement <see cref="IPackageMeta"/>.</typeparam>
public sealed class PackageBuilder<TMeta> where TMeta : PackageMetaBase, IPackageMeta, new()
{
    private readonly Dictionary<string, byte[]> _nameToBytes = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    /// <summary>
    /// The metadata to encode into the package. When <see langword="null"/>, <see cref="Build"/>
    /// uses a default-constructed <typeparamref name="TMeta"/> (entry directory only). Set this
    /// to carry type-specific fields (e.g. a save's player name and timestamp).
    /// </summary>
    public TMeta? Meta { get; set; }

    /// <summary>
    /// Master key for optional AES-256-GCM encryption (v2 format). When <see langword="null"/>
    /// (default) the package is written unencrypted (v1 format). Must be exactly 32 bytes.
    /// A fresh random salt is generated per build, so encrypted output is non-deterministic.
    /// </summary>
    public byte[]? EncryptionKey { get; set; }

    /// <summary>
    /// Content-relative alignment in bytes applied to the start of every entry (each entry is
    /// padded up so the following entry starts aligned). Must be a power of two ≥ 1. Default 1
    /// packs entries back-to-back with no padding. Use 16 or 256 for payloads intended for
    /// direct GPU upload/binding. Readers are unaffected: they use the recorded entry offsets.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when set to a non-power-of-two or &lt; 1.</exception>
    public int EntryAlignment
    {
        get => _entryAlignment;
        set
        {
            if (value < 1 || (value & (value - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Entry alignment must be a power of two >= 1.");
            }

            _entryAlignment = value;
        }
    }

    private int _entryAlignment = 1;

    /// <summary>
    /// Adds a new entry or updates an existing entry's content.
    /// </summary>
    /// <param name="entryName">Logical entry name (e.g., virtual path)</param>
    /// <param name="data">Entry bytes</param>
    public void AddOrUpdateFile(string entryName, ReadOnlySpan<byte> data)
    {
        if (string.IsNullOrEmpty(entryName))
        {
            throw new ArgumentException("Entry name must not be null or empty.", nameof(entryName));
        }

        byte[] owned = data.ToArray();
        if (_nameToBytes.ContainsKey(entryName))
        {
            _nameToBytes[entryName] = owned;
        }
        else
        {
            _nameToBytes.Add(entryName, owned);
            _order.Add(entryName);
        }
    }

    /// <summary>
    /// Removes an entry by name. No-op if the entry does not exist.
    /// </summary>
    public void RemoveFile(string entryName)
    {
        if (_nameToBytes.Remove(entryName))
        {
            _order.Remove(entryName);
        }
    }

    /// <summary>
    /// Removes all entries.
    /// </summary>
    public void Clear()
    {
        _nameToBytes.Clear();
        _order.Clear();
    }

    /// <summary>
    /// Builds the package bytes:
    /// [<see cref="IPackageMeta.Magic"/>][meta length (Int64 LE)][meta payload][content payload].
    /// Entries are padded to <see cref="EntryAlignment"/> (content-relative). When
    /// <see cref="EncryptionKey"/> is set, the meta payload is a single AEAD blob and each entry
    /// is stored as encrypted 64 KiB chunks (see <c>PackageFormat.md</c>).
    /// </summary>
    /// <returns>Package bytes</returns>
    public byte[] Build()
    {
        using PreparedPackage prepared = PrepareBuild();

        int finalLength = checked((int)(12L + prepared.SectionLength + prepared.TotalStoredLength));
        byte[] package = new byte[finalLength];
        int cursor = 0;
        WritePackage(prepared, data =>
        {
            data.CopyTo(package.AsSpan(cursor));
            cursor += data.Length;
        });

        return package;
    }

    /// <summary>
    /// Builds the package directly into an output stream following the same layout as
    /// <see cref="Build()"/>: [<see cref="IPackageMeta.Magic"/>][meta length (Int64 LE)]
    /// [meta payload][content payload]. Use for payloads too large to materialize as a single
    /// managed array on top of the builder's own buffers.
    /// </summary>
    /// <param name="output">The output stream; written from its current position.</param>
    public void Build(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);

        using PreparedPackage prepared = PrepareBuild();
        WritePackage(prepared, output.Write);
    }

    /// <summary>Writes the header, meta section, and every entry's content through <paramref name="write"/>.</summary>
    private void WritePackage(PreparedPackage prepared, PackageWriter write)
    {
        Span<byte> header = stackalloc byte[12];
        TMeta.Magic.CopyTo(header[..4]);
        long headerLength = prepared.Cipher != null
            ? unchecked((long)((ulong)prepared.SectionLength | 0x8000_0000_0000_0000UL))
            : prepared.SectionLength;
        BinaryPrimitives.WriteInt64LittleEndian(header.Slice(4, 8), headerLength);
        write(header);

        if (prepared.Cipher == null)
        {
            write(prepared.MetaBytes.Span);
        }
        else
        {
            write(prepared.Salt!);
            write(prepared.EncryptedMeta!);
        }

        byte[] padding = new byte[Math.Max(0, _entryAlignment - 1)];
        byte[]? chunkCiphertext = prepared.Cipher != null ? new byte[PackageCipher.ChunkSize] : null;
        byte[]? chunkTag = prepared.Cipher != null ? new byte[PackageCipher.TagSize] : null;

        for (int i = 0; i < _order.Count; i++)
        {
            if (!_nameToBytes.TryGetValue(_order[i], out byte[]? bytes))
            {
                continue; // Should not happen, but tolerate
            }

            long storedLength;
            if (prepared.Cipher == null)
            {
                write(bytes);
                storedLength = bytes.Length;
            }
            else
            {
                storedLength = WriteEncryptedEntry(prepared, i, bytes, chunkCiphertext!, chunkTag!, write);
            }

            int pad = AlignUp((int)storedLength, _entryAlignment) - (int)storedLength;
            if (pad > 0)
            {
                write(padding.AsSpan(0, pad));
            }
        }
    }

    /// <summary>
    /// Writes one entry as consecutive AEAD chunks (ciphertext then tag per chunk) and returns
    /// the entry's stored byte length.
    /// </summary>
    private long WriteEncryptedEntry(
        PreparedPackage prepared,
        int entryIndex,
        byte[] bytes,
        byte[] chunkCiphertext,
        byte[] chunkTag,
        PackageWriter write)
    {
        long entryStart = prepared.Meta.Entries[entryIndex].Start;
        int offset = 0;
        int chunkIndex = 0;
        while (offset < bytes.Length)
        {
            int chunkLength = Math.Min(PackageCipher.ChunkSize, bytes.Length - offset);
            prepared.Cipher!.EncryptChunk(entryStart, chunkIndex, bytes.AsSpan(offset, chunkLength),
                chunkCiphertext.AsSpan(0, chunkLength), chunkTag);
            write(chunkCiphertext.AsSpan(0, chunkLength));
            write(chunkTag);
            offset += chunkLength;
            chunkIndex++;
        }

        return PackageCipher.StoredEntryLength(bytes.Length);
    }

    /// <summary>
    /// Compute entry offsets (content-relative, honoring <see cref="EntryAlignment"/> and, when
    /// encrypting, the per-chunk tag overhead), encode the meta payload, and derive the cipher.
    /// Shared by both Build overloads.
    /// </summary>
    private PreparedPackage PrepareBuild()
    {
        byte[]? encryptionKey = EncryptionKey;
        if (encryptionKey != null && encryptionKey.Length != PackageCipher.KeySize)
        {
            throw new InvalidOperationException(
                $"EncryptionKey must be exactly {PackageCipher.KeySize} bytes, got {encryptionKey.Length}.");
        }

        TMeta meta = Meta ?? new TMeta();
        meta.ClearEntries();

        long runningOffset = 0;
        long totalStoredLength = 0;
        foreach (string name in _order)
        {
            if (!_nameToBytes.TryGetValue(name, out byte[]? bytes))
            {
                continue; // Should not happen, but tolerate
            }

            int storedLength = (int)(encryptionKey != null
                ? PackageCipher.StoredEntryLength(bytes.Length)
                : bytes.Length);
            int paddedLength = AlignUp(storedLength, _entryAlignment);
            meta.AddEntry(name, runningOffset, bytes.Length);
            runningOffset += paddedLength;
            totalStoredLength += paddedLength;
        }

        if (totalStoredLength > int.MaxValue)
        {
            throw new InvalidOperationException("Content payload too large.");
        }

        ReadOnlyMemory<byte> metaBytes = BinaryParser.Encode(meta);
        if (metaBytes.Length > int.MaxValue)
        {
            throw new InvalidOperationException("Meta payload too large.");
        }

        if (encryptionKey == null)
        {
            return new PreparedPackage
            {
                Meta = meta,
                MetaBytes = metaBytes,
                Salt = null,
                EncryptedMeta = null,
                Cipher = null,
                TotalStoredLength = totalStoredLength,
            };
        }

        byte[] salt = RandomNumberGenerator.GetBytes(PackageCipher.SaltSize);
        PackageCipher cipher = PackageCipher.Create(encryptionKey, salt);
        byte[] encryptedMeta = cipher.EncryptMeta(metaBytes.Span);
        return new PreparedPackage
        {
            Meta = meta,
            MetaBytes = metaBytes,
            Salt = salt,
            EncryptedMeta = encryptedMeta,
            Cipher = cipher,
            TotalStoredLength = totalStoredLength,
        };
    }

    private int AlignUp(int value, int alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }

    /// <summary>
    /// Packs every file under <paramref name="directory"/> (recursively, entry names relative to
    /// the directory with <c>/</c> separators) into a package at <paramref name="packagePath"/>.
    /// </summary>
    /// <param name="directory">Source directory</param>
    /// <param name="packagePath">Output package path</param>
    /// <param name="encryptionKey">Optional 32-byte master key; when set the package is AES-256-GCM encrypted</param>
    public static void PackDirectory(string directory, string packagePath, byte[]? encryptionKey = null)
    {
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("Directory must not be null or empty.", nameof(directory));
        }
        if (string.IsNullOrEmpty(packagePath))
        {
            throw new ArgumentException("Package path must not be null or empty.", nameof(packagePath));
        }
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directory}");
        }

        // Gather files deterministically
        string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        var builder = new PackageBuilder<TMeta> { EncryptionKey = encryptionKey };
        foreach (string file in files)
        {
            // Compute entry name relative to root and normalize separators to '/'
            string relative = Path.GetRelativePath(directory, file);
            relative = relative.Replace('\\', '/');

            byte[] data = File.ReadAllBytes(file);
            builder.AddOrUpdateFile(relative, data);
        }

        byte[] package = builder.Build();

        string? outDir = Path.GetDirectoryName(packagePath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }
        File.WriteAllBytes(packagePath, package);
    }

    private delegate void PackageWriter(ReadOnlySpan<byte> data);

    /// <summary>Header/meta payloads and cipher state computed once per build, shared by both Build overloads.</summary>
    private sealed class PreparedPackage : IDisposable
    {
        public required TMeta Meta { get; init; }

        /// <summary>Plaintext meta payload (written in v1 packages).</summary>
        public required ReadOnlyMemory<byte> MetaBytes { get; init; }

        /// <summary>Per-package random salt (v2 only).</summary>
        public required byte[]? Salt { get; init; }

        /// <summary>Encrypted meta as <c>ciphertext || tag</c> (v2 only).</summary>
        public required byte[]? EncryptedMeta { get; init; }

        /// <summary>Package cipher (v2 only); owns the AesGcm instances for this build.</summary>
        public required PackageCipher? Cipher { get; init; }

        /// <summary>Total content-payload bytes including per-entry alignment padding.</summary>
        public required long TotalStoredLength { get; init; }

        /// <summary>Bytes covered by the header length field: meta payload (v1) or salt + encrypted meta (v2).</summary>
        public long SectionLength => Cipher != null
            ? PackageCipher.SaltSize + EncryptedMeta!.Length
            : MetaBytes.Length;

        public void Dispose()
        {
            Cipher?.Dispose();
        }
    }
}
