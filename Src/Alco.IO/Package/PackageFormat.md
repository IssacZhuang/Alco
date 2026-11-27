## Alco Package File Format

Purpose: compact two-section package (Meta + Content) with a front-loaded entry directory enabling
random/positional access to individual entries. Used by asset bundles (`PackageMeta`, magic `"alco"`)
and by save files (`SaveMetaData`, magic `"savl"`).

- Meta: a complete PackedBinary document (inner magic `APKB`), written with `elideDefaults: false`.
  The meta type implements `IPackageMeta` (which supplies the file's magic) and derives from
  `PackageMetaBase` (which owns the `_entries` directory). Older, non-`APKB` metadata is not supported.
- Content: raw entry bytes in entry order, with optional alignment padding and no per-entry framing.

Layout (little-endian):
- [0..3] Magic: 4 ASCII bytes identifying the file type (the concrete meta's `IPackageMeta.Magic`)
- [4..11] MetaLength: Int64 — size in bytes of Meta Payload
- [12..12+MetaLength-1] Meta Payload (PackedBinary)
- [12+MetaLength..end] Content Payload

Meta schema (serialized field names include underscores):
- PackageMetaBase (inherited by every package meta)
  - `_name`: string
  - `_version`: string (format version, e.g., "1.0")
  - `_entries`: list<PackageEntry>
- PackageEntry
  - `_name`: string
  - `_start`: int64 (nonnegative offset from start of Content)
  - `_size`: int64 (nonnegative length in bytes)
- Concrete metas bind additional fields after `base.OnSerialize(...)`.

Addressing:
- `ContentBase = 12 + MetaLength`
- `FileStart = ContentBase + entry._start`
- `FileSize = entry._size`

API:
- `PackageBuilder<TMeta>` / `PackageReader<TMeta>` / `PackageFileSource<TMeta>` are generic over
  `where TMeta : PackageMetaBase, IPackageMeta, new()`. The magic is resolved via generic dispatch
  on `TMeta.Magic`, so each concrete meta validates against its own magic.
- `PackageReader<TMeta>` opens over a file path, byte array, unmanaged memory, or a seekable
  `Stream` (`OpenStream`). Only the requested entry's bytes are read — meta-only reads and
  single-entry reads never touch the rest of the content.

Notes:
- Magic identifies the file type; each concrete meta declares it directly (see `IPackageMeta` remarks
  on why the interface is declared per concrete type, not via inheritance).
- Version stored in meta as string field.
- No footer. `EntryAlignment` defaults to 1 byte (no padding) and aligns starts relative to `ContentBase`.
- File and memory readers support concurrent reads with separate destination buffers. Seekable stream
  readers require external synchronization.

## Encrypted packages (v2)

A package is encrypted when built with a 32-byte master key (optional; plaintext v1 remains the
default). Encryption is AES-256-GCM with chunked content so random-offset entry reads stay cheap.

Layout (little-endian):
- [0..3] Magic (unchanged)
- [4..11] Int64 — **negative** when encrypted: high bit set, low 63 bits = `SectionLength`,
  the total size of the salt, metadata ciphertext, and tag.
- [12..27] Salt: 16 random bytes, fresh per build
- [28..ContentBase-1] Encrypted meta: PackedBinary ciphertext + 16-byte GCM tag
- [ContentBase..end] Content payload
- `ContentBase = 12 + SectionLength`

Keys and nonces:
- Two independent subkeys derived via HKDF-SHA256 from (master key, salt): one for the meta blob,
  one for content chunks.
- Meta blob: single GCM seal with a zero nonce (unique under its own subkey).
- Content chunk nonce = u64 entry `_start` ‖ u32 chunk index (positional, unique per chunk).

Entry addressing under encryption:
- `_start` still points at the entry's stored bytes (now ciphertext); `_size` is still the
  plaintext size. Stored length = `_size + 16 × ceil(_size / 64 KiB)`.
- Entries are split into consecutive 64 KiB plaintext chunks, each stored as
  ciphertext ‖ 16-byte tag; chunks are contiguous within an entry. `EntryAlignment` pads after
  the stored length exactly as in v1.
- Whole-entry reads decrypt chunk ciphertext directly into the destination buffer in place;
  ranged reads decrypt only the covering chunks into scratch space.
- GCM tags give tamper detection: modified bytes fail authentication and throw
  `InvalidDataException` (wrapped `CryptographicException`).
- Encrypted output is non-deterministic (fresh salt per build); decrypting requires the same
  master key that packed the package.
