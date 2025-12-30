## Alco Package File Format

Purpose: compact two-section asset package: Meta + Content.

- Meta: serialized with `BinaryParser` from `PackageMeta`.
- Content: raw file bytes.

Layout (little-endian):
- [0..3] Magic: "alco" (ASCII) — file type identifier
- [4..11] MetaLength: Int64 — size in bytes of Meta Payload
- [12..12+MetaLength-1] Meta Payload (bytes from `BinaryParser`)
- [12+MetaLength..end] Content Payload

Meta schema (serialized field names include underscores):
- PackageMeta
  - `_name`: string
  - `_version`: string (format version, e.g., "1.0")
  - `_entries`: list<PackageEntry>
- PackageEntry
  - `_name`: string
  - `_start`: uint64 (offset from start of Content)
  - `_size`: uint64 (length in bytes)

Addressing:
- `ContentBase = 12 + MetaLength`
- `FileStart = ContentBase + entry._start`
- `FileSize = entry._size`

Notes:
- Magic field "alco" identifies file type.
- Version stored in meta as string field.
- No footer or alignment.
- Supports concurrent reads from multiple threads. Readers use positional I/O and do not share mutable state; each thread must provide its own destination buffer.

