using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Alco.IO;

/// <summary>
/// AES-256-GCM cipher for encrypted (v2) packages. Derives independent meta and content subkeys
/// from a master key and a per-package random salt via HKDF-SHA256, encrypts the meta section as
/// a single AEAD blob, and encrypts each entry as consecutive 64 KiB AEAD chunks (ciphertext
/// followed by a 16-byte tag each). Content nonces are positional (entry start offset + chunk
/// index), and the meta blob uses a zero nonce under its own subkey, so no nonce state is stored
/// and fresh salts prevent reuse across builds. Chunking keeps random-offset entry reads
/// decrypting only the chunks they overlap. Instances are safe for concurrent use.
/// </summary>
internal sealed class PackageCipher : IDisposable
{
    /// <summary>Plaintext bytes per content chunk.</summary>
    public const int ChunkSize = 64 * 1024;

    /// <summary>GCM authentication tag size in bytes, stored after every chunk and the meta blob.</summary>
    public const int TagSize = 16;

    /// <summary>Per-package random salt size in bytes, stored in the package header.</summary>
    public const int SaltSize = 16;

    /// <summary>Master key and derived subkey size in bytes (AES-256).</summary>
    public const int KeySize = 32;

    private const int NonceSize = 12;

    private static ReadOnlySpan<byte> MetaKeyInfo => "alco-package-meta"u8;

    private static ReadOnlySpan<byte> ContentKeyInfo => "alco-package-content"u8;

    private readonly AesGcm _meta;
    private readonly AesGcm _content;

    private PackageCipher(AesGcm meta, AesGcm content)
    {
        _meta = meta;
        _content = content;
    }

    /// <summary>
    /// Creates a cipher by deriving independent meta/content subkeys from the master key and the
    /// package salt via HKDF-SHA256.
    /// </summary>
    /// <param name="masterKey">32-byte master key</param>
    /// <param name="salt">16-byte per-package salt</param>
    public static PackageCipher Create(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> salt)
    {
        if (masterKey.Length != KeySize)
        {
            throw new ArgumentException($"Master key must be exactly {KeySize} bytes.", nameof(masterKey));
        }
        if (salt.Length != SaltSize)
        {
            throw new ArgumentException($"Salt must be exactly {SaltSize} bytes.", nameof(salt));
        }

        byte[] metaKey = new byte[KeySize];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, metaKey, salt, MetaKeyInfo);
        byte[] contentKey = new byte[KeySize];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, contentKey, salt, ContentKeyInfo);
        return new PackageCipher(new AesGcm(metaKey, TagSize), new AesGcm(contentKey, TagSize));
    }

    /// <summary>Gets the number of chunks covering an entry of the given plaintext size.</summary>
    public static int ChunkCount(long plainSize)
    {
        return (int)((plainSize + ChunkSize - 1) / ChunkSize);
    }

    /// <summary>Gets the stored (on-disk) byte length of an entry of the given plaintext size:
    /// ciphertext plus one tag per chunk.</summary>
    public static long StoredEntryLength(long plainSize)
    {
        return plainSize + (long)ChunkCount(plainSize) * TagSize;
    }

    /// <summary>
    /// Encrypts the meta payload into a single <c>ciphertext || tag</c> blob.
    /// </summary>
    /// <param name="plain">Plaintext meta bytes</param>
    public byte[] EncryptMeta(ReadOnlySpan<byte> plain)
    {
        byte[] output = new byte[plain.Length + TagSize];
        Span<byte> nonce = stackalloc byte[NonceSize];
        _meta.Encrypt(nonce, plain, output.AsSpan(0, plain.Length), output.AsSpan(plain.Length));
        return output;
    }

    /// <summary>
    /// Decrypts a meta blob produced by <see cref="EncryptMeta"/>.
    /// </summary>
    /// <param name="ciphertextAndTag"><c>ciphertext || tag</c> bytes</param>
    /// <exception cref="CryptographicException">Thrown when the tag does not match (wrong key or tampering).</exception>
    public byte[] DecryptMeta(ReadOnlySpan<byte> ciphertextAndTag)
    {
        if (ciphertextAndTag.Length < TagSize)
        {
            throw new ArgumentException($"Encrypted meta must be at least {TagSize} bytes.", nameof(ciphertextAndTag));
        }

        int plainLength = ciphertextAndTag.Length - TagSize;
        byte[] plain = new byte[plainLength];
        Span<byte> nonce = stackalloc byte[NonceSize];
        _meta.Decrypt(nonce, ciphertextAndTag[..plainLength], ciphertextAndTag[plainLength..], plain);
        return plain;
    }

    /// <summary>
    /// Encrypts one content chunk. <paramref name="ciphertext"/> and <paramref name="tag"/> must
    /// have room for the chunk plaintext and <see cref="TagSize"/> bytes respectively.
    /// </summary>
    /// <param name="entryStart">Content-relative start offset of the owning entry (nonce input)</param>
    /// <param name="chunkIndex">Zero-based index of the chunk within its entry (nonce input)</param>
    /// <param name="plain">Chunk plaintext</param>
    /// <param name="ciphertext">Destination for the chunk ciphertext</param>
    /// <param name="tag">Destination for the authentication tag</param>
    public void EncryptChunk(long entryStart, int chunkIndex, ReadOnlySpan<byte> plain, Span<byte> ciphertext, Span<byte> tag)
    {
        Span<byte> nonce = stackalloc byte[NonceSize];
        WriteChunkNonce(nonce, entryStart, chunkIndex);
        _content.Encrypt(nonce, plain, ciphertext, tag);
    }

    /// <summary>
    /// Decrypts one content chunk in place: <paramref name="chunkData"/> holds the ciphertext on
    /// entry and receives the plaintext on return.
    /// </summary>
    /// <param name="entryStart">Content-relative start offset of the owning entry (nonce input)</param>
    /// <param name="chunkIndex">Zero-based index of the chunk within its entry (nonce input)</param>
    /// <param name="chunkData">Chunk ciphertext; receives the chunk plaintext</param>
    /// <param name="tag">Authentication tag of the chunk</param>
    /// <exception cref="CryptographicException">Thrown when the tag does not match (wrong key or tampering).</exception>
    public void DecryptChunk(long entryStart, int chunkIndex, Span<byte> chunkData, ReadOnlySpan<byte> tag)
    {
        Span<byte> nonce = stackalloc byte[NonceSize];
        WriteChunkNonce(nonce, entryStart, chunkIndex);
        _content.Decrypt(nonce, chunkData, tag, chunkData);
    }

    public void Dispose()
    {
        _meta.Dispose();
        _content.Dispose();
    }

    /// <summary>Builds the positional content nonce: u64 entry start ‖ u32 chunk index.</summary>
    private static void WriteChunkNonce(Span<byte> nonce, long entryStart, int chunkIndex)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[..8], (ulong)entryStart);
        BinaryPrimitives.WriteUInt32LittleEndian(nonce[8..], (uint)chunkIndex);
    }
}
