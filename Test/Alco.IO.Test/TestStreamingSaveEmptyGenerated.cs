using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Alco;
using Alco.IO;
using NUnit.Framework;

namespace Alco.IO.Test;

/// <summary>
/// Dual-backend tests covering <see cref="ChunkIndexFlags.EmptyGenerated"/> across spill, segments, compaction, and <c>.asav</c> archives.
/// </summary>
[TestFixture]
public sealed class TestStreamingSaveEmptyGenerated
{
    public enum StorageKind
    {
        InMemory,
        Directory
    }

    private readonly List<string> _tempDirectories = [];

    [TearDown]
    public void CleanupTempDirectories()
    {
        for (int i = 0; i < _tempDirectories.Count; i++)
        {
            string dir = _tempDirectories[i];
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of test temp folder.
            }
        }

        _tempDirectories.Clear();
    }

    [TestCase(StorageKind.InMemory)]
    [TestCase(StorageKind.Directory)]
    public void SpillStore_MarkEmpty_ReadsZeroBytes_AvoidsFileCreation_AndDrainsMixedAndEmptyOnly(StorageKind kind)
    {
        IRandomAccessStorage storage = CreateStorage(kind);
        Guid worldGuid = Guid.NewGuid();
        using var codecs = new ChunkCodecSet();
        using var segStore = new ChunkSegmentStore(storage, worldGuid, codecs);

        var emptyKeyA = new ChunkKey(1, 0, 0);
        var emptyKeyB = new ChunkKey(1, 2, 1);
        var dataKey = new ChunkKey(1, 1, 0);
        byte[] payload = Encoding.UTF8.GetBytes(new string('E', 512));

        // 1. Empty-only spill does not touch storage handles and drains cleanly without creating segments.
        using (var emptySpill = new SpillStore(storage, worldGuid, Guid.NewGuid(), codecs))
        {
            Assert.Throws<ArgumentException>(() => emptySpill.WriteChunk(emptyKeyA, [], (byte)ChunkIndexFlags.EmptyGenerated));

            emptySpill.MarkEmpty(emptyKeyA);
            emptySpill.MarkEmpty(emptyKeyB, (byte)ChunkIndexFlags.DirtyLight);

            Assert.That(emptySpill.Contains(emptyKeyA), Is.True);
            Assert.That(storage.EnumerateFiles(StreamingSavePaths.SessionDirectory(worldGuid), "*"), Is.Empty);

            var readBuf = new ArrayBufferWriter<byte>(64);
            Assert.That(emptySpill.TryReadChunk(emptyKeyB, readBuf, out ChunkIndexEntry readEntry, out string? readErr), Is.EqualTo(ChunkReadStatus.Success), readErr);
            Assert.That(readBuf.WrittenCount, Is.EqualTo(0));
            Assert.That(readEntry.IsValidEmptyGenerated, Is.True);
            Assert.That((readEntry.Flags & (byte)ChunkIndexFlags.DirtyLight) != 0, Is.True);

            ChunkIndexEntry[] drained = emptySpill.DrainTo(segStore, ChunkCodecId.Lz4, flushToDisk: true);
            Assert.That(drained.Length, Is.EqualTo(2));
            Assert.That(drained[0].Key, Is.EqualTo(emptyKeyA));
            Assert.That(drained[1].Key, Is.EqualTo(emptyKeyB));
            Assert.That(drained[0].IsValidEmptyGenerated, Is.True);
            Assert.That(drained[1].IsValidEmptyGenerated, Is.True);
            Assert.That(storage.EnumerateFiles(StreamingSavePaths.ChunksDirectory(worldGuid), StreamingSavePaths.SegmentFilePattern), Is.Empty);
        }

        // 2. Mixed empty + non-empty + overwrite transitions in SpillStore.
        using (var mixedSpill = new SpillStore(storage, worldGuid, Guid.NewGuid(), codecs))
        {
            mixedSpill.WriteChunk(emptyKeyA, payload);
            mixedSpill.MarkEmpty(emptyKeyA); // Overwrites previous spilled record with EmptyGenerated.
            mixedSpill.WriteChunk(dataKey, payload, (byte)ChunkIndexFlags.HasEntities);
            mixedSpill.MarkEmpty(emptyKeyB);

            ChunkIndexEntry[] drained = mixedSpill.DrainTo(segStore, ChunkCodecId.Lz4, flushToDisk: true);
            Assert.That(drained.Length, Is.EqualTo(3));
            Assert.That(drained[0].Key, Is.EqualTo(emptyKeyA));
            Assert.That(drained[0].IsValidEmptyGenerated, Is.True);
            Assert.That(drained[1].Key, Is.EqualTo(dataKey));
            Assert.That(drained[1].IsEmptyGenerated, Is.False);
            Assert.That(drained[1].SegmentId, Is.GreaterThanOrEqualTo(1u));
            Assert.That(drained[2].Key, Is.EqualTo(emptyKeyB));
            Assert.That(drained[2].IsValidEmptyGenerated, Is.True);

            var readBuf = new ArrayBufferWriter<byte>(512);
            Assert.That(segStore.TryReadChunk(drained[0], readBuf, out string? emptyReadErr), Is.True, emptyReadErr);
            Assert.That(readBuf.WrittenCount, Is.EqualTo(0));

            Assert.That(segStore.TryReadChunk(drained[1], readBuf, out string? dataReadErr), Is.True, dataReadErr);
            Assert.That(readBuf.WrittenSpan.SequenceEqual(payload), Is.True);
        }
    }

    [TestCase(StorageKind.InMemory)]
    [TestCase(StorageKind.Directory)]
    public void Snapshot_Compaction_And_AsavArchive_PreserveEmptyGeneratedChunks(StorageKind kind)
    {
        IRandomAccessStorage storage = CreateStorage(kind);
        Guid worldGuid = Guid.NewGuid();
        using var codecs = new ChunkCodecSet();
        using var segStore = new ChunkSegmentStore(storage, worldGuid, codecs);
        var snapStore = new SnapshotStore<EmptyTestMeta>(storage, codecs);

        var deadKey = new ChunkKey(1, 0, 0);
        var keepKey = new ChunkKey(1, 1, 0);
        var emptyKey = new ChunkKey(1, 3, 2);
        byte[] deadPayload = Encoding.UTF8.GetBytes(new string('D', 1024));
        byte[] keepPayload = Encoding.UTF8.GetBytes(new string('K', 1024));

        // Slot 1 uses deadKey; Slot 2 replaces deadKey with EmptyGenerated and adds keepKey.
        ChunkIndexEntry[] slot1Committed = segStore.AppendBatch([new ChunkWriteInput(deadKey, deadPayload)], ChunkCodecId.Lz4);
        snapStore.WriteSnapshot(new SnapshotId(worldGuid, "Slot1"), new EmptyTestMeta(1), Encoding.UTF8.GetBytes("core1"), slot1Committed);

        ChunkIndexEntry[] keepCommitted = segStore.AppendBatch([new ChunkWriteInput(keepKey, keepPayload)], ChunkCodecId.Lz4);
        ChunkIndexEntry[] mixedIndex =
        [
            keepCommitted[0],
            ChunkIndexEntry.CreateEmptyGenerated(emptyKey),
            ChunkIndexEntry.CreateEmptyGenerated(deadKey)
        ];
        var mixedSlot = new SnapshotId(worldGuid, "SlotMixed");
        snapStore.WriteSnapshot(mixedSlot, new EmptyTestMeta(2), Encoding.UTF8.GetBytes("core2"), mixedIndex);

        // Also verify SnapshotFormat rejects malformed EmptyGenerated (non-zero SegmentId or CompressedSize).
        var malformedEmpty = new ChunkIndexEntry(1, 0, 0, segmentId: 1, 0, 0, 0, ChunkCodecId.None, (byte)ChunkIndexFlags.EmptyGenerated);
        Assert.Throws<ArgumentException>(() => SnapshotFormat.SortAndValidateChunkIndex([malformedEmpty]));

        // Compact after deleting Slot1: keepKey stays intact, emptyKey stays EmptyGenerated without throwing missing segment 0.
        Assert.That(snapStore.DeleteSnapshot(new SnapshotId(worldGuid, "Slot1")), Is.True);
        CompactionReport compactReport = snapStore.CompactWorld(segStore);
        Assert.That(compactReport.Executed, Is.True);

        Assert.That(snapStore.TryLoadSnapshot(mixedSlot, out SnapshotLoadResult<EmptyTestMeta>? loadedMixed, out string? loadErr), Is.True, loadErr);
        Assert.That(loadedMixed!.ChunkIndex.Count, Is.EqualTo(3));
        Assert.That(loadedMixed.ChunkIndex[0].Key, Is.EqualTo(deadKey));
        Assert.That(loadedMixed.ChunkIndex[0].IsValidEmptyGenerated, Is.True);
        Assert.That(loadedMixed.ChunkIndex[1].Key, Is.EqualTo(keepKey));
        Assert.That(loadedMixed.ChunkIndex[1].IsEmptyGenerated, Is.False);
        Assert.That(loadedMixed.ChunkIndex[2].Key, Is.EqualTo(emptyKey));
        Assert.That(loadedMixed.ChunkIndex[2].IsValidEmptyGenerated, Is.True);

        // Export mixed snapshot to .asav, inspect via AsavArchiveReader, and import into a fresh world.
        using var mixedArchive = new InMemoryBufferHandle();
        Assert.That(AsavArchive.TryExport(snapStore, mixedSlot, segStore, mixedArchive, codecs, out string? exportErr, flushToDisk: false), Is.True, exportErr);

        using (var readerOpened = AsavArchiveReader<EmptyTestMeta>.TryOpenHandle(mixedArchive, ownsHandle: false, codecs, out AsavArchiveReader<EmptyTestMeta>? reader, out string? openErr)
            ? reader!
            : throw new AssertionException(openErr))
        {
            Assert.That(readerOpened.ChunkIndex.Count, Is.EqualTo(3));
            var readBuf = new ArrayBufferWriter<byte>(1024);
            Assert.That(readerOpened.TryReadChunk(readerOpened.ChunkIndex[0], readBuf, out string? err0), Is.True, err0);
            Assert.That(readBuf.WrittenCount, Is.EqualTo(0));

            Assert.That(readerOpened.TryReadChunk(readerOpened.ChunkIndex[1], readBuf, out string? err1), Is.True, err1);
            Assert.That(readBuf.WrittenSpan.SequenceEqual(keepPayload), Is.True);

            readBuf.Clear();
            Assert.That(readerOpened.TryReadCompressedChunk(readerOpened.ChunkIndex[2], readBuf, out string? err2), Is.True, err2);
            Assert.That(readBuf.WrittenCount, Is.EqualTo(0));
        }

        Guid importedWorld = Guid.NewGuid();
        using var importSegStore = new ChunkSegmentStore(storage, importedWorld, codecs);
        Assert.That(AsavArchive.TryImport(mixedArchive, snapStore, importSegStore, "ImportedMixed", codecs, out SnapshotMetaInfo<EmptyTestMeta> importedMeta, out string? impErr, flushToDisk: false), Is.True, impErr);
        Assert.That(snapStore.TryLoadSnapshot(importedMeta.Id, out SnapshotLoadResult<EmptyTestMeta>? importedLoaded, out string? impLoadErr), Is.True, impLoadErr);
        Assert.That(importedLoaded!.ChunkIndex.Count, Is.EqualTo(3));
        Assert.That(importedLoaded.ChunkIndex[0].IsValidEmptyGenerated, Is.True);
        Assert.That(importedLoaded.ChunkIndex[1].IsEmptyGenerated, Is.False);
        Assert.That(importedLoaded.ChunkIndex[2].IsValidEmptyGenerated, Is.True);

        var importedData = new ArrayBufferWriter<byte>(1024);
        Assert.That(importSegStore.TryReadChunk(importedLoaded.ChunkIndex[1], importedData, out string? impReadErr), Is.True, impReadErr);
        Assert.That(importedData.WrittenSpan.SequenceEqual(keepPayload), Is.True);

        // Empty-only snapshot: export, import into a clean world without creating segments, and compact old world removes all old segments.
        var emptyOnlySlot = new SnapshotId(worldGuid, "SlotEmptyOnly");
        snapStore.WriteSnapshot(
            emptyOnlySlot,
            new EmptyTestMeta(3),
            Encoding.UTF8.GetBytes("core3"),
            [ChunkIndexEntry.CreateEmptyGenerated(emptyKey)]);

        using var emptyArchive = new InMemoryBufferHandle();
        Assert.That(AsavArchive.TryExport(snapStore, emptyOnlySlot, segStore, emptyArchive, codecs, out string? emptyExportErr, flushToDisk: false), Is.True, emptyExportErr);

        Guid emptyImportWorld = Guid.NewGuid();
        using var emptyImportSegStore = new ChunkSegmentStore(storage, emptyImportWorld, codecs);
        Assert.That(AsavArchive.TryImport(emptyArchive, snapStore, emptyImportSegStore, "ImportedEmptyOnly", codecs, out SnapshotMetaInfo<EmptyTestMeta> importedEmptyMeta, out string? emptyImpErr, flushToDisk: false), Is.True, emptyImpErr);
        Assert.That(storage.EnumerateFiles(StreamingSavePaths.ChunksDirectory(emptyImportWorld), StreamingSavePaths.SegmentFilePattern), Is.Empty);
        Assert.That(snapStore.TryLoadSnapshot(importedEmptyMeta.Id, out SnapshotLoadResult<EmptyTestMeta>? loadedEmptyOnly, out string? emptyLoadErr), Is.True, emptyLoadErr);
        Assert.That(loadedEmptyOnly!.ChunkIndex.Count, Is.EqualTo(1));
        Assert.That(loadedEmptyOnly.ChunkIndex[0].IsValidEmptyGenerated, Is.True);

        // Deleting SlotMixed leaves only SlotEmptyOnly in worldGuid -> CompactWorld deletes remaining segment(s).
        Assert.That(snapStore.DeleteSnapshot(mixedSlot), Is.True);
        CompactionReport emptyCleanupReport = snapStore.CompactWorld(segStore);
        Assert.That(emptyCleanupReport.Executed, Is.True);
        Assert.That(storage.EnumerateFiles(StreamingSavePaths.ChunksDirectory(worldGuid), StreamingSavePaths.SegmentFilePattern), Is.Empty);
    }

    private IRandomAccessStorage CreateStorage(StorageKind kind) =>
        kind == StorageKind.InMemory
            ? new InMemoryRandomAccessStorage()
            : new DirectoryRandomAccessStorage(CreateTempDirectory());

    private string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"AlcoIoEmptyTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private sealed class EmptyTestMeta : ISerializable
    {
        private int _version;

        public EmptyTestMeta()
        {
        }

        public EmptyTestMeta(int version)
        {
            _version = version;
        }

        public int Version => _version;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindValue(nameof(_version), ref _version);
        }
    }
}
