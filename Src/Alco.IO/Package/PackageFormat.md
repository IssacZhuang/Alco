## Alco Package File Format

Purpose: compact two-section package (Meta + Content) with a front-loaded entry directory enabling
random/positional access to individual entries. Used by asset bundles (`PackageMeta`, magic `"alco"`)
and by save files (`SaveMetaData`, magic `"savl"`).

- Meta: serialized with `BinaryParser`. The meta type implements `IPackageMeta` (which supplies the
  file's magic) and derives from `PackageMetaBase` (which owns the `_entries` directory). A concrete
  meta may add its own fields beyond the inherited directory.
- Content: raw entry bytes, concatenated in entry order (no per-entry framing).

Layout (little-endian):
- [0..3] Magic: 4 ASCII bytes identifying the file type (the concrete meta's `IPackageMeta.Magic`)
- [4..11] MetaLength: Int64 — size in bytes of Meta Payload
- [12..12+MetaLength-1] Meta Payload (bytes from `BinaryParser`)
- [12+MetaLength..end] Content Payload

Meta schema (serialized field names include underscores):
- PackageMetaBase (inherited by every package meta)
  - `_name`: string
  - `_version`: string (format version, e.g., "1.0")
  - `_entries`: list<PackageEntry>
- PackageEntry
  - `_name`: string
  - `_start`: uint64 (offset from start of Content)
  - `_size`: uint64 (length in bytes)
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
- No footer or alignment.
- Supports concurrent reads from multiple threads. Readers use positional I/O and do not share
mutable state; each thread must provide its own destination buffer.
