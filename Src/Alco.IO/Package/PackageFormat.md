## Alco Package File Format

Purpose: single-file container with named entries supporting both sealed builds
(`PackageBuilder<TMeta>`, one-shot, zero garbage) and incremental updates
(`PackageWriter<TMeta>`, append-only commits). Used by asset bundles (`PackageMeta`, magic
`"alco"`) and by save files (`SaveMetaData`, magic `"savl"`).

The layout is designed for streaming reads: entries are addressed by content-relative offset and
read positionally — reading one entry never touches any other entry's bytes.

- Meta (the entry directory): serialized with `BinaryParser`. The meta type implements
  `IPackageMeta` (which supplies the file's magic) and derives from `PackageMetaBase` (which owns
  the `_entries` directory). A concrete meta may add its own fields beyond the inherited directory.
- Content: raw entry bytes, appended over time (no per-entry framing; replaced entries become
  garbage until compaction).

Layout (little-endian):

```
[0..63]    Fixed header
             [0..3]   Magic: 4 ASCII bytes (the concrete meta's IPackageMeta.Magic)
             [4..7]   Format version: UInt32 (currently 1)
             [8..31]  Directory descriptor A: [offset Int64][length UInt32][hash UInt64][sequence UInt32]
             [32..55] Directory descriptor B: same 24 bytes
             [56..63] Reserved (zeros)
[64..]     Content: entry bytes, appended; entry starts are content-relative (base = 64)
[tail]     Directory payload: BinaryParser-encoded meta, referenced by exactly one descriptor
```

Header invariants:
- Exactly one descriptor is "active" at any time — the one with the highest valid sequence. A
  descriptor with sequence 0 is unused. Descriptors must satisfy: `offset >= 64`,
  `offset + length <= fileLength`, and `XxHash64(payload) == hash`.
- `PackageBuilder` seals with descriptor A (sequence 1) and B unused.
- `PackageWriter` alternates: every commit appends the new directory at the end of the file, then
  patches the stale descriptor with the new offset/length/hash and sequence = previous + 1. The
  sequence is the last 4 bytes of the descriptor so a torn in-place patch can never promote a
  partially written descriptor over the committed one.
- Crash safety: entry bytes and the directory are flushed to disk before the descriptor patch. A
  crash before/during the patch leaves the previous descriptor authoritative; partially appended
  bytes are garbage reclaimed by compaction. At worst one commit is lost, never the package.

Meta schema (serialized field names include underscores):
- PackageMetaBase (inherited by every package meta)
  - `_name`: string
  - `_version`: string (format version, e.g., "1.0")
  - `_entries`: list<PackageEntry>
- PackageEntry
  - `_name`: string
  - `_start`: uint64 (offset from start of Content, i.e. from byte 64)
  - `_size`: uint64 (length in bytes)
  - `_checksum`: uint64 (XxHash64 of the entry's content bytes; verified by readers only when
    `PackageReader.VerifyEntryChecksums` is enabled)
- Concrete metas bind additional fields after `base.OnSerialize(...)`.

Addressing:
- `ContentBase = 64` (`PackageFormat.HeaderSize`)
- `FileStart = ContentBase + entry._start`
- `FileSize = entry._size`

API:
- `PackageBuilder<TMeta>` / `PackageReader<TMeta>` / `PackageWriter<TMeta>` /
  `PackageFileSource<TMeta>` are generic over `where TMeta : PackageMetaBase, IPackageMeta, new()`.
  The magic is resolved via generic dispatch on `TMeta.Magic`, so each concrete meta validates
  against its own magic.
- `PackageReader<TMeta>` opens over a file path, byte array, unmanaged memory, or a seekable
  `Stream` (`OpenStream`). Opening reads the 64-byte header plus one directory payload; reads are
  positional per entry. Falls back to the older descriptor when the newest fails validation.
- `PackageWriter<TMeta>` opens the file read/write with `FileShare.Read`; commits are serialized
  internally and readers may open concurrently. `CompactFile(path)` rewrites all live entries into
  the sealed layout (via `PackageBuilder`) into a temp file and atomically `File.Replace`s the
  original; all handles must be closed first.

Notes:
- Magic identifies the file type; each concrete meta declares it directly (see `IPackageMeta`
  remarks on why the interface is declared per concrete type, not via inheritance).
- Meta-level version string (`_version`) is independent of the header's numeric format version.
- Garbage = fileLength − 64 − live entry bytes − current directory size; reclaimed only by
  `CompactFile`.
- Pre-v1 packages (magic + Int64 meta length + front-loaded meta) are rejected by the version
  check with `InvalidDataException`; re-save them with the current build to migrate.
- Supports concurrent reads from multiple threads. Readers use positional I/O and do not share
  mutable state; each thread must provide its own destination buffer.
