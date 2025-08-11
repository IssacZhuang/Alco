## Alco Package File Format

Purpose: compact two-section asset package: Meta + Content.

- Meta: serialized with `BinaryParser` from `PackageMeta`.
- Content: raw file bytes.

Layout (little-endian):
- [0..7] MetaLength: Int64 — size in bytes of Meta Payload
- [8..8+MetaLength-1] Meta Payload (bytes from `BinaryParser`)
- [8+MetaLength..end] Content Payload

Meta schema (serialized field names include underscores):
- PackageMeta
  - `_name`: string
  - `_entries`: list<PackageEntry>
- PackageEntry
  - `_name`: string
  - `_start`: uint64 (offset from start of Content)
  - `_size`: uint64 (length in bytes)

Addressing:
- `ContentBase = 8 + MetaLength`
- `FileStart = ContentBase + entry._start`
- `FileSize = entry._size`

Notes:
- No footer, alignment, or version field.

