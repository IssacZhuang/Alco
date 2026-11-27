using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alco;
using Alco.IO;
using NUnit.Framework;

namespace Alco.IO.Test;

/// <summary>
/// Dual-backend tests covering streaming chunk segments, spill logs, snapshots, compaction GC, and <c>.asav</c> archives.
/// </summary>
[TestFixture]
public sealed class TestStreamingSaveStorage
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

    [Test]
    public void ChunkIndexEntry_Is32Bytes_AndChunkKey_OrdersByMapThenMorton()
    {
        Assert.That(Marshal.SizeOf<ChunkIndexEntry>(), Is.EqualTo(32));

        var keys = new[]
        {
            new ChunkKey(2, 0, 0),
            new ChunkKey(1, 3, 2),
            new ChunkKey(1, -2, -1),
            new ChunkKey(1, -1, -1),
            new ChunkKey(1, 0, 0)
        };
        Array.Sort(keys);

        for (int i = 1; i < keys.Length; i++)
        {
            Assert.That(keys[i - 1].CompareTo(keys[i]), Is.LessThan(0));
            if (keys[i - 1].MapId == keys[i].MapId)
                Assert.That(keys[i - 1].MortonCode, Is.LessThan(keys[i].MortonCode));
        }
    }

    [TestCase(StorageKind.InMemory)]
    [TestCase(StorageKind.Directory)]
    public void Storage_PathSafety_And_InodeAtomicSemantics(StorageKind kind)
    {
        IRandomAccessStorage storage = CreateStorage(kind);

        Assert.Throws<ArgumentException>(() => storage.FileExists("../escape.bin"));
        Assert.Throws<ArgumentException>(() => storage.FileExists("/rooted/file.bin"));
        Assert.Throws<ArgumentException>(() => storage.FileExists("C:/bad/file.bin"));
        Assert.Throws<ArgumentException>(() => storage.EnumerateFiles("worlds/..", "*.snap"));

        byte[] v1 = Encoding.UTF8.GetBytes("snapshot-version-1");
        byte[] v2 = Encoding.UTF8.GetBytes("snapshot-version-2-longer");
        storage.WriteAtomic("worlds/test/file.bin", v1, flushToDisk: false);

        if (kind == StorageKind.InMemory)
        {
            Assert.That(storage.TryOpenRead("worlds/test/file.bin", out IRandomAccessHandle? oldHandle), Is.True);
            using (oldHandle!)
            {
                storage.WriteAtomic("worlds/test/file.bin", v2, flushToDisk: false);

                byte[] readOld = new byte[oldHandle!.Length];
                Assert.That(oldHandle.ReadAt(0, readOld), Is.EqualTo(v1.Length));
                Assert.That(readOld, Is.EqualTo(v1));
            }
        }
        else
        {
            storage.WriteAtomic("worlds/test/file.bin", v2, flushToDisk: false);
        }

        Assert.That(storage.TryReadAllBytes("worlds/test/file.bin", out byte[]? currentBytes), Is.True);
        Assert.That(currentBytes, Is.EqualTo(v2));

        // Verify FileShare.Delete semantics: deleting while a read handle is open preserves readability until disposed.
        Assert.That(storage.TryOpenRead("worlds/test/file.bin", out IRandomAccessHandle? handleDuringDelete), Is.True);
        using (handleDuringDelete!)
        {
            Assert.That(storage.DeleteFile("worlds/test/file.bin"), Is.True);
            Assert.That(storage.FileExists("worlds/test/file.bin"), Is.False);

            byte[] readAfterDelete = new byte[handleDuringDelete!.Length];
            Assert.That(handleDuringDelete.ReadAt(0, readAfterDelete), Is.EqualTo(v2.Length));
            Assert.That(readAfterDelete, Is.EqualTo(v2));
        }

        storage.WriteAtomic("a/one.txt", [1], false);
        storage.WriteAtomic("a/sub/two.txt", [2], false);
        Assert.That(storage.EnumerateFiles("a", "*.txt", recursive: false), Is.EqualTo(new[] { "a/one.txt" }));
        Assert.That(storage.EnumerateFiles("a", "*.txt", recursive: true), Is.EqualTo(new[] { "a/one.txt", "a/sub/two.txt" }));
        Assert.That(storage.EnumerateDirectories("a"), Is.EqualTo(new[] { "a/sub" }));
    }

    [TestCase(ChunkCodecId.None)]
    [TestCase(ChunkCodecId.Lz4)]
    [TestCase(ChunkCodecId.Zstd)]
    public void ChunkCodec_RoundTripsEmptyRepetitiveAndRandom_AndRejectsCorruption(ChunkCodecId codecId)
    {
        using var codecs = new ChunkCodecSet();

        byte[][] samples =
        [
            [],
            CreateRepetitivePayload(16 * 1024, seed: 7),
            CreateRandomPayload(4096, seed: 99)
        ];

        for (int i = 0; i < samples.Length; i++)
        {
            byte[] src = samples[i];
            byte[] compBuf = new byte[Math.Max(1, codecs.GetMaxCompressedLength(codecId, src.Length))];
            int compLen = codecs.Compress(codecId, src, compBuf);

            byte[] restored = new byte[src.Length];
            Assert.That(codecs.TryDecompress(codecId, compBuf.AsSpan(0, compLen), restored, out int written), Is.True);
            Assert.That(written, Is.EqualTo(src.Length));
            Assert.That(restored, Is.EqualTo(src));

            if (src.Length > 0)
            {
                Assert.That(codecs.TryDecompress(codecId, compBuf.AsSpan(0, compLen), new byte[src.Length - 1], out _), Is.False);
            }
        }

        if (codecId != ChunkCodecId.None)
        {
            byte[] corrupt = [0xAA, 0xBB, 0xCC, 0xDD, 0x11, 0x22];
            Assert.That(codecs.TryDecompress(codecId, corrupt, new byte[64], out _), Is.False);
            Assert.That(codecs.TryDecompress(codecId, [], new byte[8], out _), Is.False);
        }
    }

    [Test]
    public void ChunkPayload_RoundTrips_And_RejectsInvalidOrDuplicateSections()
    {
        var builder = new ChunkPayloadBuilder { LastSimTime = 1234.5 };
        byte[] emptyPayload = builder.ToArray();
        Assert.That(emptyPayload.Length, Is.EqualTo(ChunkRecordCodec.ChunkPayloadHeaderSize));
        Assert.That(ChunkPayloadReader.TryOpen(emptyPayload, out ChunkPayloadReader emptyReader), Is.True);
        Assert.That(emptyReader.SectionCount, Is.EqualTo(0));
        Assert.That(emptyReader.LastSimTime, Is.EqualTo(1234.5));

        builder.Reset(42.25);
        builder.AddSection(10, [1, 2, 3, 4], flags: 0x05);
        using (ChunkPayloadBuilder.SectionScope aborted = builder.BeginSection(99))
        {
            aborted.Writer.Write<byte>([99, 98, 97]);
        }

        using (ChunkPayloadBuilder.SectionScope committed = builder.BeginSection(20, flags: 0x12))
        {
            committed.Writer.Write<byte>([10, 20, 30]);
            committed.Commit();
        }

        Assert.Throws<ArgumentException>(() => builder.AddSection(10, [1]));
        Assert.Throws<ArgumentException>(() => builder.BeginSection(10));
        byte[] built = builder.ToArray();

        Assert.That(ChunkPayloadReader.TryOpen(built, out ChunkPayloadReader reader), Is.True);
        Assert.That(reader.LastSimTime, Is.EqualTo(42.25));
        Assert.That(reader.SectionCount, Is.EqualTo(2));
        Assert.That(reader.TryGetSection(10, out ReadOnlySpan<byte> sec10, out ushort flags10), Is.True);
        Assert.That(flags10, Is.EqualTo(0x05));
        Assert.That(sec10.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));

        Assert.That(reader.TryGetSection(20, out ReadOnlySpan<byte> sec20, out ushort flags20), Is.True);
        Assert.That(flags20, Is.EqualTo(0x12));
        Assert.That(sec20.ToArray(), Is.EqualTo(new byte[] { 10, 20, 30 }));
        Assert.That(reader.TryGetSection(999, out _, out _), Is.False);

        var enumerated = new List<(ushort Id, ushort Flags, byte[] Data)>();
        foreach (ChunkSection section in reader)
            enumerated.Add((section.Id, section.Flags, section.Data.ToArray()));
        Assert.That(enumerated.Count, Is.EqualTo(2));
        Assert.That(enumerated[0].Id, Is.EqualTo(10));
        Assert.That(enumerated[1].Id, Is.EqualTo(20));
        Assert.That(enumerated[1].Flags, Is.EqualTo(0x12));
        Assert.That(enumerated[1].Data, Is.EqualTo(new byte[] { 10, 20, 30 }));

        // Corrupt duplicate section ID inside raw ACHK bytes must be rejected by TryOpen.
        byte[] dupPayload = (byte[])built.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(dupPayload.AsSpan(ChunkRecordCodec.ChunkPayloadHeaderSize + ChunkRecordCodec.ChunkSectionDescSize, 2), 10);
        Assert.That(ChunkPayloadReader.TryOpen(dupPayload, out _), Is.False);

        // Truncated payload must be rejected.
        Assert.That(ChunkPayloadReader.TryOpen(built.AsSpan(0, built.Length - 1), out _), Is.False);

        // > 64 sections exercises heap-backed duplicate check.
        builder.Reset(9.0);
        for (ushort s = 0; s < 72; s++)
            builder.AddSection(s, [(byte)s]);
        byte[] manySections = builder.ToArray();
        Assert.That(ChunkPayloadReader.TryOpen(manySections, out ChunkPayloadReader manyReader), Is.True);
        Assert.That(manyReader.SectionCount, Is.EqualTo(72));
    }

    [Test]
    public void ChunkPayload_SectionScope_AutoAbortsOnMissingCommitOrException_AndRejectsDuplicateEagerly()
    {
        var builder = new ChunkPayloadBuilder();

        using (ChunkPayloadBuilder.SectionScope missingCommit = builder.BeginSection(5))
        {
            missingCommit.Writer.Write<byte>([1, 2, 3, 4, 5]);
        }

        Assert.That(builder.SectionCount, Is.EqualTo(0));
        Assert.That(builder.ToArray().Length, Is.EqualTo(ChunkRecordCodec.ChunkPayloadHeaderSize));

        Assert.Throws<InvalidOperationException>(() =>
        {
            using ChunkPayloadBuilder.SectionScope failingScope = builder.BeginSection(6);
            failingScope.Writer.Write<byte>([9, 8, 7]);
            throw new InvalidOperationException("Simulated write abort.");
        });

        Assert.That(builder.SectionCount, Is.EqualTo(0));

        builder.AddSection(7, [42]);
        Assert.Throws<ArgumentException>(() => builder.BeginSection(7));

        Assert.That(ChunkPayloadReader.TryOpen(builder.ToArray(), out ChunkPayloadReader reader), Is.True);
        Assert.That(reader.SectionCount, Is.EqualTo(1));
        Assert.That(reader.TryGetSection(7, out ReadOnlySpan<byte> data, out _), Is.True);
        Assert.That(data.ToArray(), Is.EqualTo(new byte[] { 42 }));
    }

    [TestCase(StorageKind.InMemory)]
    [TestCase(StorageKind.Directory)]
    public void ChunkSegmentStore_MultiSegmentMixedCodecs_And_8ThreadConcurrentReads(StorageKind kind)
    {
        IRandomAccessStorage storage = CreateStorage(kind);
        Guid worldGuid = Guid.NewGuid();
        var expectedByKey = new Dictionary<ChunkKey, byte[]>();
        var allEntries = new List<ChunkIndexEntry>();
        using var codecs = new ChunkCodecSet();

        using (var store = new ChunkSegmentStore(storage, worldGuid, codecs) { MaxSegmentBytes = 1536 })
        {
            var batch1 = new List<ChunkWriteInput>();
            var batch2 = new List<ChunkWriteInput>();
            var batch3 = new List<ChunkWriteInput>();

            for (int i = 0; i < 18; i++)
            {
                var key = new ChunkKey((uint)(i % 2 + 1), i - 9, (i * 3) - 20);
                byte[] payload = CreateRepetitivePayload(600 + i * 25, seed: 100 + i);
                expectedByKey[key] = payload;
                var input = new ChunkWriteInput(key, payload, Flags: (byte)(i & 0x0F));

                if (i < 6) batch1.Add(input);
                else if (i < 12) batch2.Add(input);
                else batch3.Add(input);
            }

            allEntries.AddRange(store.AppendBatch(CollectionsMarshal.AsSpan(batch1), ChunkCodecId.None, flushToDisk: false));
            allEntries.AddRange(store.AppendBatch(CollectionsMarshal.AsSpan(batch2), ChunkCodecId.Lz4, flushToDisk: false));
            allEntries.AddRange(store.AppendBatch(CollectionsMarshal.AsSpan(batch3), ChunkCodecId.Zstd, flushToDisk: true));
        }

        IReadOnlyList<string> segmentFiles = storage.EnumerateFiles(StreamingSavePaths.ChunksDirectory(worldGuid), StreamingSavePaths.SegmentFilePattern);
        Assert.That(segmentFiles.Count, Is.GreaterThanOrEqualTo(3));

        // Verify 16B alignment and reopening preserves monotonic segment numbering without overwriting old segments.
        for (int i = 0; i < allEntries.Count; i++)
            Assert.That(allEntries[i].RecordOffset % 16, Is.EqualTo(0));

        using var reopenedStore = new ChunkSegmentStore(storage, worldGuid, codecs);
        var extraKey = new ChunkKey(3, -100, -50);
        byte[] extraPayload = CreateRandomPayload(512, seed: 777);
        expectedByKey[extraKey] = extraPayload;
        ChunkIndexEntry[] appendedAfterReopen = reopenedStore.AppendBatch([new ChunkWriteInput(extraKey, extraPayload)], ChunkCodecId.Lz4);
        Assert.That(appendedAfterReopen[0].SegmentId, Is.GreaterThan(segmentFiles.Count));
        allEntries.Add(appendedAfterReopen[0]);

        Parallel.For(0, 8, workerIdx =>
        {
            var rng = new Random(1000 + workerIdx);
            var writer = new ArrayBufferWriter<byte>(4096);
            for (int iter = 0; iter < 60; iter++)
            {
                ChunkIndexEntry entry = allEntries[rng.Next(allEntries.Count)];
                writer.Clear();
                Assert.That(reopenedStore.TryReadChunk(entry, writer, out string? error), Is.True, error);
                Assert.That(writer.WrittenSpan.SequenceEqual(expectedByKey[entry.Key]), Is.True);
            }
        });
    }

    [Test]
    public void ChunkSegmentStore_Dispose_DoesNotDisposeSharedCodecSet()
    {
        var storage = new InMemoryRandomAccessStorage();
        Guid worldGuid = Guid.NewGuid();
        using var codecs = new ChunkCodecSet();

        ChunkIndexEntry entry;
        using (var segStore = new ChunkSegmentStore(storage, worldGuid, codecs))
        {
            entry = segStore.AppendBatch([new ChunkWriteInput(new ChunkKey(1, 0, 0), CreateRepetitivePayload(256, seed: 3))], ChunkCodecId.Zstd)[0];
            var readBuf = new ArrayBufferWriter<byte>();
            Assert.That(segStore.TryReadChunk(entry, readBuf, out _), Is.True);
        }

        byte[] sample = CreateRepetitivePayload(512, seed: 9);
        byte[] comp = new byte[codecs.GetMaxCompressedLength(ChunkCodecId.Zstd, sample.Length)];
        int compLen = codecs.Compress(ChunkCodecId.Zstd, sample, comp);
        byte[] roundTrip = new byte[sample.Length];
        Assert.That(codecs.TryDecompress(ChunkCodecId.Zstd, comp.AsSpan(0, compLen), roundTrip, out int written), Is.True);
        Assert.That(written, Is.EqualTo(sample.Length));
        Assert.That(roundTrip, Is.EqualTo(sample));
    }

    [TestCase(StorageKind.InMemory)]
    [TestCase(StorageKind.Directory)]
    public void FaultInjection_InterceptsCorruptedSegmentsSnapshotsAndArchives(StorageKind kind)
    {
        IRandomAccessStorage storage = CreateStorage(kind);
        Guid worldGuid = Guid.NewGuid();
        using var codecs = new ChunkCodecSet();
        var key = new ChunkKey(1, -3, 4);
        byte[] chunkPayload = CreateRepetitivePayload(1024, seed: 55);
        byte[] corePayload = CreateRepetitivePayload(512, seed: 66);
        byte[] thumbPng = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4];

        ChunkIndexEntry[] entries;
        string segPath;
        using (var segStore = new ChunkSegmentStore(storage, worldGuid, codecs))
        {
            entries = segStore.AppendBatch([new ChunkWriteInput(key, chunkPayload)], ChunkCodecId.Lz4);
            segPath = StreamingSavePaths.SegmentFilePath(worldGuid, entries[0].SegmentId);
            Assert.That(segStore.TryReadChunk(entries[0], new ArrayBufferWriter<byte>(), out _), Is.True);
        }

        Assert.That(storage.TryReadAllBytes(segPath, out byte[]? originalSeg), Is.True);

        byte[] badSegHeader = (byte[])originalSeg!.Clone();
        badSegHeader[0] ^= 0x01;
        storage.WriteAtomic(segPath, badSegHeader, flushToDisk: false);
        using (var freshStore = new ChunkSegmentStore(storage, worldGuid, codecs))
        {
            Assert.That(freshStore.TryReadChunk(entries[0], new ArrayBufferWriter<byte>(), out _), Is.False);
        }

        byte[] badRecHeader = (byte[])originalSeg.Clone();
        badRecHeader[entries[0].RecordOffset + 5] ^= 0x40;
        storage.WriteAtomic(segPath, badRecHeader, flushToDisk: false);
        using (var freshStore = new ChunkSegmentStore(storage, worldGuid, codecs))
        {
            Assert.That(freshStore.TryReadChunk(entries[0], new ArrayBufferWriter<byte>(), out _), Is.False);
        }

        byte[] badRecPayload = (byte[])originalSeg.Clone();
        badRecPayload[entries[0].RecordOffset + 32] ^= 0x80;
        storage.WriteAtomic(segPath, badRecPayload, flushToDisk: false);
        using (var freshStore = new ChunkSegmentStore(storage, worldGuid, codecs))
        {
            Assert.That(freshStore.TryReadChunk(entries[0], new ArrayBufferWriter<byte>(), out _), Is.False);
        }

        storage.WriteAtomic(segPath, originalSeg.AsSpan(0, (int)entries[0].RecordOffset + 32 + (int)entries[0].CompressedSize + 4), flushToDisk: false);
        using (var freshStore = new ChunkSegmentStore(storage, worldGuid, codecs))
        {
            Assert.That(freshStore.TryReadChunk(entries[0], new ArrayBufferWriter<byte>(), out _), Is.False);
        }

        // Restore valid segment before snapshot & archive tests.
        storage.WriteAtomic(segPath, originalSeg, flushToDisk: false);

        // 2. Corrupt snapshot header (CRC32), oversized length (OOM guard), core, and trailing XxHash64.
        var snapStore = new SnapshotStore<TestSnapshotMeta>(storage, codecs);
        var snapId = new SnapshotId(worldGuid, "Slot_Fault");
        snapStore.WriteSnapshot(snapId, new TestSnapshotMeta("Player", 10, 2.5), corePayload, entries, thumbPng);
        string snapPath = StreamingSavePaths.SnapshotFilePath(worldGuid, snapId.SlotId);
        Assert.That(storage.TryReadAllBytes(snapPath, out byte[]? originalSnap), Is.True);

        byte[] badSnapHeader = (byte[])originalSnap!.Clone();
        badSnapHeader[29] ^= 0x01; // Flip MetaLength bit -> HeaderCrc32 catches it.
        storage.WriteAtomic(snapPath, badSnapHeader, flushToDisk: false);
        Assert.That(snapStore.TryReadMeta(snapId, out _, out _, out _), Is.False);
        Assert.That(snapStore.TryReadThumbnail(snapId, out _, out _), Is.False);
        Assert.That(snapStore.TryLoadSnapshot(snapId, out _, out _), Is.False);

        // Forge huge CoreUncompressedLength with valid CRC32 -> OOM limit rejects before allocating.
        byte[] hugeCoreSnap = (byte[])originalSnap.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(hugeCoreSnap.AsSpan(48, 4), uint.MaxValue);
        uint updatedCrc = System.IO.Hashing.Crc32.HashToUInt32(hugeCoreSnap.AsSpan(0, 60));
        BinaryPrimitives.WriteUInt32LittleEndian(hugeCoreSnap.AsSpan(60, 4), updatedCrc);
        ulong updatedWholeHash = System.IO.Hashing.XxHash64.HashToUInt64(hugeCoreSnap.AsSpan(0, hugeCoreSnap.Length - 8));
        BinaryPrimitives.WriteUInt64LittleEndian(hugeCoreSnap.AsSpan(hugeCoreSnap.Length - 8, 8), updatedWholeHash);
        storage.WriteAtomic(snapPath, hugeCoreSnap, flushToDisk: false);
        Assert.That(snapStore.TryLoadSnapshot(snapId, out _, out _), Is.False);

        // Corrupt trailing XxHash64 on valid snapshot.
        byte[] badSnapTrailer = (byte[])originalSnap.Clone();
        badSnapTrailer[^1] ^= 0x01;
        storage.WriteAtomic(snapPath, badSnapTrailer, flushToDisk: false);
        Assert.That(snapStore.TryLoadSnapshot(snapId, out _, out _), Is.False);

        // Corrupt core byte -> WholeFileXxHash64 rejects.
        byte[] badSnapCore = (byte[])originalSnap.Clone();
        uint coreOff = BinaryPrimitives.ReadUInt32LittleEndian(badSnapCore.AsSpan(40, 4));
        badSnapCore[coreOff] ^= 0x01;
        storage.WriteAtomic(snapPath, badSnapCore, flushToDisk: false);
        Assert.That(snapStore.TryLoadSnapshot(snapId, out _, out _), Is.False);

        // Restore valid snapshot, export .asav, and inject .asav faults.
        storage.WriteAtomic(snapPath, originalSnap, flushToDisk: false);
        using var exportMem = new InMemoryBufferHandle();
        using (var exportSegStore = new ChunkSegmentStore(storage, worldGuid, codecs))
        {
            Assert.That(AsavArchive.TryExport(snapStore, snapId, exportSegStore, exportMem, codecs, out string? exportErr, flushToDisk: false), Is.True, exportErr);
        }
        byte[] originalAsav = exportMem.ToArray();

        // Corrupt trailing XxHash64.
        byte[] badAsavTrailer = (byte[])originalAsav.Clone();
        badAsavTrailer[^3] ^= 0x10;
        using (IRandomAccessHandle h = InMemoryBufferHandle.CreateReadOnly(badAsavTrailer))
        {
            Assert.That(AsavArchiveReader<TestSnapshotMeta>.TryOpenHandle(h, ownsHandle: false, codecs, out _, out _), Is.False);
            Guid importWorld = Guid.NewGuid();
            using var importStore = new ChunkSegmentStore(storage, importWorld, codecs);
            Assert.That(AsavArchive.TryImport(h, snapStore, importStore, "ImpBad", codecs, out _, out _), Is.False);
        }

        // Corrupt chunk payload inside .asav while recomputing whole-file hash -> per-chunk XxHash64 stops import before writing segments!
        byte[] badAsavChunk = (byte[])originalAsav.Clone();
        uint chunkDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(badAsavChunk.AsSpan(28, 4));
        badAsavChunk[chunkDataOffset + 32] ^= 0xFF;
        ulong newWholeHash = System.IO.Hashing.XxHash64.HashToUInt64(badAsavChunk.AsSpan(0, badAsavChunk.Length - 8));
        BinaryPrimitives.WriteUInt64LittleEndian(badAsavChunk.AsSpan(badAsavChunk.Length - 8, 8), newWholeHash);
        using (IRandomAccessHandle h = InMemoryBufferHandle.CreateReadOnly(badAsavChunk))
        {
            Guid importWorld = Guid.NewGuid();
            using var importStore = new ChunkSegmentStore(storage, importWorld, codecs);
            Assert.That(AsavArchive.TryImport(h, snapStore, importStore, "ImpBadChunk", codecs, out _, out _), Is.False);
            Assert.That(storage.EnumerateFiles(StreamingSavePaths.ChunksDirectory(importWorld), StreamingSavePaths.SegmentFilePattern), Is.Empty);
        }
    }

    [TestCase(StorageKind.InMemory)]
    [TestCase(StorageKind.Directory)]
    public void MultiSlotIsolation_And_CompactionGC_PreservesAllVersionsAndHandlesConcurrentReads(StorageKind kind)
    {
        IRandomAccessStorage storage = CreateStorage(kind);
        Guid worldGuid = Guid.NewGuid();
        using var codecs = new ChunkCodecSet();

        using var segStore = new ChunkSegmentStore(storage, worldGuid, codecs) { MaxSegmentBytes = 2048 };
        var snapStore = new SnapshotStore<TestSnapshotMeta>(storage, codecs);

        var slotAData = new Dictionary<ChunkKey, byte[]>();
        var slotBData = new Dictionary<ChunkKey, byte[]>();

        var inputsA = new ChunkWriteInput[10];
        for (int i = 0; i < 10; i++)
        {
            var key = new ChunkKey(1, i - 4, i % 3 - 1);
            byte[] data = CreateRepetitivePayload(700 + i * 20, seed: 200 + i);
            slotAData[key] = data;
            slotBData[key] = data;
            inputsA[i] = new ChunkWriteInput(key, data);
        }

        ChunkIndexEntry[] entriesA = segStore.AppendBatch(inputsA, ChunkCodecId.Lz4, flushToDisk: true);
        var slotA = new SnapshotId(worldGuid, "Slot_A");
        snapStore.WriteSnapshot(slotA, new TestSnapshotMeta("A", 1, 10.0), Encoding.UTF8.GetBytes("coreA"), entriesA);

        // Modify first 4 chunks and write Slot B.
        var modifiedInputs = new ChunkWriteInput[4];
        for (int i = 0; i < 4; i++)
        {
            ChunkKey key = entriesA[i].Key;
            byte[] newData = CreateRepetitivePayload(750 + i * 30, seed: 500 + i);
            slotBData[key] = newData;
            modifiedInputs[i] = new ChunkWriteInput(key, newData);
        }

        ChunkIndexEntry[] modifiedEntries = segStore.AppendBatch(modifiedInputs, ChunkCodecId.None, flushToDisk: true);
        var entriesBMap = new Dictionary<ChunkKey, ChunkIndexEntry>();
        for (int i = 0; i < entriesA.Length; i++)
            entriesBMap[entriesA[i].Key] = entriesA[i];
        for (int i = 0; i < modifiedEntries.Length; i++)
            entriesBMap[modifiedEntries[i].Key] = modifiedEntries[i];

        var entriesB = new ChunkIndexEntry[entriesBMap.Count];
        entriesBMap.Values.CopyTo(entriesB, 0);
        var slotB = new SnapshotId(worldGuid, "Slot_B");
        snapStore.WriteSnapshot(slotB, new TestSnapshotMeta("B", 2, 20.0), Encoding.UTF8.GetBytes("coreB"), entriesB);

        // Force compaction with BOTH Slot A and Slot B live: distinct versions of same ChunkKeys must survive!
        CompactionReport dualReport = snapStore.CompactWorld(
            segStore,
            options: new CompactionOptions { Force = true, TargetCodec = ChunkCodecId.Zstd });
        Assert.That(dualReport.Executed, Is.True);
        Assert.That(dualReport.RewrittenChunks, Is.EqualTo(14));

        AssertSnapshotChunksMatch(snapStore, segStore, slotA, slotAData, expectedCodec: ChunkCodecId.Zstd);
        AssertSnapshotChunksMatch(snapStore, segStore, slotB, slotBData, expectedCodec: ChunkCodecId.Zstd);

        // Verify compacted segments carry SegmentFlags.Compacted.
        IReadOnlyList<string> segsAfterDual = storage.EnumerateFiles(StreamingSavePaths.ChunksDirectory(worldGuid), StreamingSavePaths.SegmentFilePattern);
        for (int i = 0; i < segsAfterDual.Count; i++)
        {
            Assert.That(StreamingSavePaths.TryParseSegmentId(segsAfterDual[i], out uint segId), Is.True);
            Assert.That(storage.TryOpenRead(segsAfterDual[i], out IRandomAccessHandle? h), Is.True);
            using (h!)
            {
                Assert.That(ChunkRecordCodec.TryReadAndValidateSegmentHeader(h!, worldGuid, segId, false, out SegmentFlags flags, out _), Is.True);
                Assert.That(flags.HasFlag(SegmentFlags.Compacted), Is.True);
            }
        }

        // Write more garbage overwrite rounds and delete Slot B so dead ratio exceeds 40%, then run concurrent 8T reads + auto-threshold compaction.
        for (int round = 0; round < 3; round++)
        {
            var garbage = new ChunkWriteInput[10];
            for (int i = 0; i < 10; i++)
                garbage[i] = new ChunkWriteInput(new ChunkKey(2, i, round), CreateRandomPayload(900, seed: 900 + round * 20 + i));
            segStore.AppendBatch(garbage, ChunkCodecId.None, flushToDisk: true);
        }

        Assert.That(snapStore.DeleteSnapshot(slotB), Is.True);
        Assert.That(snapStore.TryLoadSnapshot(slotA, out SnapshotLoadResult<TestSnapshotMeta>? snapABefore, out _), Is.True);
        ChunkIndexEntry[] initialIndex = [.. snapABefore!.ChunkIndex];
        ChunkIndexEntry[] currentReadableIndex = (ChunkIndexEntry[])initialIndex.Clone();

        using var stopReaders = new CancellationTokenSource();
        Task[] readerTasks = new Task[8];
        for (int t = 0; t < 8; t++)
        {
            int seed = 700 + t;
            readerTasks[t] = Task.Run(() =>
            {
                var rng = new Random(seed);
                var writer = new ArrayBufferWriter<byte>(2048);
                while (!stopReaders.IsCancellationRequested)
                {
                    ChunkIndexEntry[] snapshot = Volatile.Read(ref currentReadableIndex);
                    ChunkIndexEntry entry = snapshot[rng.Next(snapshot.Length)];
                    writer.Clear();
                    if (segStore.TryReadChunk(entry, writer, out _))
                    {
                        Assert.That(writer.WrittenSpan.SequenceEqual(slotAData[entry.Key]), Is.True);
                    }
                }
            });
        }

        // Let readers warm up handles before retiring segments.
        Thread.Sleep(25);
        CompactionReport gcReport = snapStore.CompactWorld(
            segStore,
            onEntriesRelocated: relocs =>
            {
                var updated = (ChunkIndexEntry[])initialIndex.Clone();
                for (int i = 0; i < updated.Length; i++)
                {
                    if (relocs.TryGetValue(updated[i].Location, out ChunkIndexEntry next))
                        updated[i] = next;
                }

                Volatile.Write(ref currentReadableIndex, updated);
            },
            options: new CompactionOptions { Force = false, DeadRatioThreshold = 0.40, TargetCodec = ChunkCodecId.Zstd });

        Thread.Sleep(25);
        stopReaders.Cancel();
        Task.WaitAll(readerTasks);

        Assert.That(gcReport.Executed, Is.True);
        Assert.That(gcReport.RemovedSegments, Is.GreaterThanOrEqualTo(1));
        AssertSnapshotChunksMatch(snapStore, segStore, slotA, slotAData, expectedCodec: ChunkCodecId.Zstd);

        // Deleting last snapshot + CompactWorld cleans all segments and empty world folders.
        Assert.That(snapStore.DeleteSnapshot(slotA), Is.True);
        CompactionReport finalReport = snapStore.CompactWorld(segStore);
        Assert.That(finalReport.Executed, Is.True);
        Assert.That(storage.EnumerateFiles(StreamingSavePaths.ChunksDirectory(worldGuid), StreamingSavePaths.SegmentFilePattern), Is.Empty);
        if (kind == StorageKind.Directory)
        {
            var dirStorage = (DirectoryRandomAccessStorage)storage;
            Assert.That(Directory.Exists(Path.Combine(dirStorage.RootDirectory, "worlds", worldGuid.ToString("N"))), Is.False);
        }
    }

    [Test]
    public void SnapshotStore_CompactWorld_PreservesExactMetaCoreThumbnail_WithoutDecodingMeta()
    {
        var storage = new InMemoryRandomAccessStorage();
        Guid worldGuid = Guid.NewGuid();
        using var codecs = new ChunkCodecSet();
        using var segStore = new ChunkSegmentStore(storage, worldGuid, codecs);
        var snapStore = new SnapshotStore<TestSnapshotMeta>(storage, codecs);

        var liveKey1 = new ChunkKey(1, 0, 0);
        var liveKey2 = new ChunkKey(1, 2, -1);
        byte[] payload1 = CreateRepetitivePayload(640, seed: 11);
        byte[] payload2 = CreateRepetitivePayload(720, seed: 22);
        ChunkIndexEntry[] deadEntries = segStore.AppendBatch([new ChunkWriteInput(liveKey1, CreateRandomPayload(512, seed: 99))], ChunkCodecId.None, flushToDisk: false);
        Assert.That(deadEntries.Length, Is.EqualTo(1));

        ChunkIndexEntry[] liveEntries = segStore.AppendBatch(
            [new ChunkWriteInput(liveKey1, payload1), new ChunkWriteInput(liveKey2, payload2)],
            ChunkCodecId.None,
            flushToDisk: false);

        var expectedMeta = new TestSnapshotMeta("CompactOwner", 42, 9876.125);
        byte[] expectedCore = CreateRandomPayload(1536, seed: 333);
        byte[] expectedThumb = [0x89, 0x50, 0x4E, 0x47, 5, 6, 7, 8, 9];
        var slotId = new SnapshotId(worldGuid, "KeepBytesExact");
        snapStore.WriteSnapshot(slotId, expectedMeta, expectedCore, liveEntries, expectedThumb, coreCodec: ChunkCodecId.None, flushToDisk: false);

        CompactionReport report = snapStore.CompactWorld(
            segStore,
            options: new CompactionOptions { Force = true, TargetCodec = ChunkCodecId.Zstd, FlushToDisk = false });
        Assert.That(report.Executed, Is.True);
        Assert.That(report.RewrittenChunks, Is.EqualTo(2));

        Assert.That(snapStore.TryReadThumbnail(slotId, out byte[]? loadedThumb, out _), Is.True);
        Assert.That(loadedThumb, Is.EqualTo(expectedThumb));

        Assert.That(snapStore.TryLoadSnapshot(slotId, out SnapshotLoadResult<TestSnapshotMeta>? reloaded, out _, includeThumbnail: true), Is.True);
        Assert.That(reloaded!.Meta, Is.EqualTo(expectedMeta));
        Assert.That(reloaded.CoreCodec, Is.EqualTo(ChunkCodecId.None));
        Assert.That(reloaded.CoreBytes.Span.SequenceEqual(expectedCore), Is.True);
        Assert.That(reloaded.ThumbnailBytes.Span.SequenceEqual(expectedThumb), Is.True);
        AssertSnapshotChunksMatch(
            snapStore,
            segStore,
            slotId,
            new Dictionary<ChunkKey, byte[]> { [liveKey1] = payload1, [liveKey2] = payload2 },
            expectedCodec: ChunkCodecId.Zstd);
    }

    [TestCase(StorageKind.InMemory)]
    [TestCase(StorageKind.Directory)]
    public void SpillStore_LazyLifecycle_RollbackIsolation_CorruptionGuard_And_Drain(StorageKind kind)
    {
        IRandomAccessStorage storage = CreateStorage(kind);
        Guid worldGuid = Guid.NewGuid();
        Guid sessionGuid = Guid.NewGuid();
        string spillPath = StreamingSavePaths.SpillFilePath(worldGuid, sessionGuid);
        using var codecs = new ChunkCodecSet();

        using var segStore = new ChunkSegmentStore(storage, worldGuid, codecs);
        var snapStore = new SnapshotStore<TestSnapshotMeta>(storage, codecs);

        var key1 = new ChunkKey(1, 0, 0);
        var key2 = new ChunkKey(1, 1, 0);
        byte[] committed1 = CreateRepetitivePayload(800, seed: 11);
        byte[] committed2 = CreateRepetitivePayload(800, seed: 22);
        ChunkIndexEntry[] baseEntries = segStore.AppendBatch(
            [new ChunkWriteInput(key1, committed1), new ChunkWriteInput(key2, committed2)],
            ChunkCodecId.Lz4);

        var baseline = new Dictionary<ChunkKey, ChunkIndexEntry>
        {
            [key1] = baseEntries[0],
            [key2] = baseEntries[1]
        };
        var slotId = new SnapshotId(worldGuid, "Manual_Base");
        snapStore.WriteSnapshot(slotId, new TestSnapshotMeta("Hero", 1, 5.0), Encoding.UTF8.GetBytes("core"), baseEntries);

        using (var spill = new SpillStore(storage, worldGuid, sessionGuid, codecs, ChunkCodecId.Lz4))
        {
            Assert.That(storage.FileExists(spillPath), Is.False);

            byte[] dirty1A = CreateRepetitivePayload(900, seed: 33);
            byte[] dirty1B = CreateRepetitivePayload(950, seed: 44);
            spill.WriteChunk(key1, dirty1A);
            spill.WriteChunk(key1, dirty1B);
            Assert.That(storage.FileExists(spillPath), Is.True);
            Assert.That(spill.SpilledChunkCount, Is.EqualTo(1));

            // Spill takes precedence for key1; key2 falls back cleanly to committed segment store.
            var readBuf = new ArrayBufferWriter<byte>();
            Assert.That(TryReadWithSpillFallback(spill, key1, baseline, segStore, readBuf, out string? err1), Is.True, err1);
            Assert.That(readBuf.WrittenSpan.SequenceEqual(dirty1B), Is.True);

            readBuf.Clear();
            Assert.That(TryReadWithSpillFallback(spill, key2, baseline, segStore, readBuf, out string? err2), Is.True, err2);
            Assert.That(readBuf.WrittenSpan.SequenceEqual(committed2), Is.True);

            // Rollback without saving: loading prior snapshot still returns committed1 cleanly.
            Assert.That(snapStore.TryLoadSnapshot(slotId, out SnapshotLoadResult<TestSnapshotMeta>? reloaded, out _), Is.True);
            readBuf.Clear();
            Assert.That(segStore.TryReadChunk(reloaded!.ChunkIndex[0], readBuf, out _), Is.True);
            Assert.That(readBuf.WrittenSpan.SequenceEqual(committed1), Is.True);

            // Drain with excludeKeys leaves excluded key in spill and commits remaining keys.
            byte[] dirty2 = CreateRepetitivePayload(820, seed: 55);
            spill.WriteChunk(key2, dirty2);
            ChunkIndexEntry[] drainedPartial = spill.DrainTo(
                segStore,
                targetCodec: ChunkCodecId.Zstd,
                excludeKeys: new HashSet<ChunkKey> { key1 });
            Assert.That(drainedPartial.Length, Is.EqualTo(1));
            Assert.That(drainedPartial[0].Key, Is.EqualTo(key2));
            Assert.That(drainedPartial[0].Codec, Is.EqualTo(ChunkCodecId.Zstd));
            Assert.That(spill.Contains(key1), Is.True);
            Assert.That(spill.Contains(key2), Is.False);

            readBuf.Clear();
            Assert.That(segStore.TryReadChunk(drainedPartial[0], readBuf, out _), Is.True);
            Assert.That(readBuf.WrittenSpan.SequenceEqual(dirty2), Is.True);

            // Corrupt spill file on disk -> spill returns Failed and must NOT fall back to stale committed1.
            Assert.That(spill.TryReadChunk(key1, new ArrayBufferWriter<byte>(), out ChunkIndexEntry spilledEntry, out _), Is.EqualTo(ChunkReadStatus.Success));
            Assert.That(storage.TryOpenRead(spillPath, out IRandomAccessHandle? spillReadHandle), Is.True);
            byte[] spillBytes = new byte[spillReadHandle!.Length];
            spillReadHandle.ReadAt(0, spillBytes);
            spillReadHandle.Dispose();

            // Write corrupted copy via OpenAppendOrCreate to mutate the spilled record payload.
            using (IRandomAccessHandle writeCorrupt = storage.OpenAppendOrCreate(spillPath))
            {
                byte flipped = (byte)(spillBytes[spilledEntry.RecordOffset + 32] ^ 0xFF);
                writeCorrupt.WriteAt(spilledEntry.RecordOffset + 32, [flipped]);
            }

            readBuf.Clear();
            Assert.That(spill.TryReadChunk(key1, readBuf, out _, out _), Is.EqualTo(ChunkReadStatus.Failed));
            readBuf.Clear();
            Assert.That(TryReadWithSpillFallback(spill, key1, baseline, segStore, readBuf, out _), Is.False);
        }

        // Dispose cleaned spill file and empty session directory.
        Assert.That(storage.FileExists(spillPath), Is.False);
        if (kind == StorageKind.Directory)
        {
            var dirStorage = (DirectoryRandomAccessStorage)storage;
            Assert.That(Directory.Exists(Path.Combine(dirStorage.RootDirectory, "worlds", worldGuid.ToString("N"), "session")), Is.False);
        }
    }

    [Test]
    public void SpillStore_DrainTo_BatchesLargeDrains_AndPreservesConcurrentOverwrite()
    {
        var innerStorage = new InMemoryRandomAccessStorage();
        Guid worldGuid = Guid.NewGuid();
        using var codecs = new ChunkCodecSet();

        // Part 1: verify multi-batch draining across the 4 MiB internal drain batch threshold.
        using (var segStore = new ChunkSegmentStore(innerStorage, worldGuid, codecs))
        using (var largeSpill = new SpillStore(innerStorage, worldGuid, Guid.NewGuid(), codecs, ChunkCodecId.None))
        {
            var expected = new Dictionary<ChunkKey, byte[]>();
            for (int i = 0; i < 5; i++)
            {
                var key = new ChunkKey(1, i, 0);
                byte[] payload = CreateRepetitivePayload(1100 * 1024, seed: 30 + i);
                expected[key] = payload;
                largeSpill.WriteChunk(key, payload);
            }

            ChunkIndexEntry[] drained = largeSpill.DrainTo(segStore, ChunkCodecId.None, flushToDisk: false);
            Assert.That(drained.Length, Is.EqualTo(5));
            Assert.That(largeSpill.SpilledChunkCount, Is.EqualTo(0));

            var verifyBuf = new ArrayBufferWriter<byte>(1100 * 1024);
            for (int i = 0; i < drained.Length; i++)
            {
                verifyBuf.Clear();
                Assert.That(segStore.TryReadChunk(drained[i], verifyBuf, out string? err), Is.True, err);
                Assert.That(verifyBuf.WrittenSpan.SequenceEqual(expected[drained[i].Key]), Is.True);
            }
        }

        // Part 2: simulate concurrent WriteChunk overwrite on key0 while FlushDrainBatch writes target segment.
        Guid concurrentWorld = Guid.NewGuid();
        var key0 = new ChunkKey(2, 0, 0);
        var key1 = new ChunkKey(2, 1, 0);
        byte[] old0 = CreateRepetitivePayload(512, seed: 1);
        byte[] old1 = CreateRepetitivePayload(512, seed: 2);
        byte[] newer0 = CreateRepetitivePayload(768, seed: 99);

        using var concurrentSpill = new SpillStore(innerStorage, concurrentWorld, Guid.NewGuid(), codecs, ChunkCodecId.Lz4);
        concurrentSpill.WriteChunk(key0, old0);
        concurrentSpill.WriteChunk(key1, old1);

        bool injectedOverwrite = false;
        var interceptingStorage = new InterceptingAppendStorage(
            innerStorage,
            relativePath =>
            {
                if (!injectedOverwrite && relativePath.EndsWith(".chk", StringComparison.Ordinal))
                {
                    injectedOverwrite = true;
                    concurrentSpill.WriteChunk(key0, newer0);
                }
            });

        using var targetSegStore = new ChunkSegmentStore(interceptingStorage, concurrentWorld, codecs);
        ChunkIndexEntry[] firstDrain = concurrentSpill.DrainTo(targetSegStore, ChunkCodecId.Lz4, flushToDisk: false);
        Assert.That(injectedOverwrite, Is.True);
        Assert.That(firstDrain.Length, Is.EqualTo(2));

        // Newer key0 write must be retained in spill store instead of removed by stale drain completion!
        Assert.That(concurrentSpill.SpilledChunkCount, Is.EqualTo(1));
        Assert.That(concurrentSpill.Contains(key0), Is.True);
        Assert.That(concurrentSpill.Contains(key1), Is.False);

        var spillBuf = new ArrayBufferWriter<byte>();
        Assert.That(concurrentSpill.TryReadChunk(key0, spillBuf, out _, out _), Is.EqualTo(ChunkReadStatus.Success));
        Assert.That(spillBuf.WrittenSpan.SequenceEqual(newer0), Is.True);

        ChunkIndexEntry[] secondDrain = concurrentSpill.DrainTo(targetSegStore, ChunkCodecId.Lz4, flushToDisk: false);
        Assert.That(secondDrain.Length, Is.EqualTo(1));
        Assert.That(secondDrain[0].Key, Is.EqualTo(key0));
        Assert.That(concurrentSpill.SpilledChunkCount, Is.EqualTo(0));

        spillBuf.Clear();
        Assert.That(targetSegStore.TryReadChunk(secondDrain[0], spillBuf, out _), Is.True);
        Assert.That(spillBuf.WrittenSpan.SequenceEqual(newer0), Is.True);
    }

    [TestCase(StorageKind.InMemory)]
    [TestCase(StorageKind.Directory)]
    public void Asav_ExportStreamReadAndImport_RoundTripsAllDataAndZeroChunkWorld(StorageKind kind)
    {
        IRandomAccessStorage storage = CreateStorage(kind);
        Guid srcWorld = Guid.NewGuid();
        Guid dstWorld = Guid.NewGuid();
        using var codecs = new ChunkCodecSet();

        using var srcSegStore = new ChunkSegmentStore(storage, srcWorld, codecs) { MaxSegmentBytes = 1024 };
        using var dstSegStore = new ChunkSegmentStore(storage, dstWorld, codecs);
        var snapStore = new SnapshotStore<TestSnapshotMeta>(storage, codecs);

        var expectedChunks = new Dictionary<ChunkKey, byte[]>();
        var listNone = new List<ChunkWriteInput>();
        var listLz4 = new List<ChunkWriteInput>();
        for (int i = 0; i < 8; i++)
        {
            var key = new ChunkKey((uint)(i % 2 + 1), i - 4, -i);
            byte[] payload = CreateRepetitivePayload(500 + i * 40, seed: 300 + i);
            expectedChunks[key] = payload;
            var item = new ChunkWriteInput(key, payload, Flags: (byte)(i + 1));
            if (i % 2 == 0) listNone.Add(item);
            else listLz4.Add(item);
        }

        var srcEntries = new List<ChunkIndexEntry>();
        srcEntries.AddRange(srcSegStore.AppendBatch(CollectionsMarshal.AsSpan(listNone), ChunkCodecId.None));
        srcEntries.AddRange(srcSegStore.AppendBatch(CollectionsMarshal.AsSpan(listLz4), ChunkCodecId.Lz4));

        byte[] core = CreateRandomPayload(1024, seed: 888);
        byte[] thumb = [0x89, 0x50, 0x4E, 0x47, 10, 20, 30, 40];
        var srcMeta = new TestSnapshotMeta("Alice", 99, 3600.5);
        var srcId = new SnapshotId(srcWorld, "Manual_Export");
        snapStore.WriteSnapshot(srcId, srcMeta, core, CollectionsMarshal.AsSpan(srcEntries), thumb);

        // Export to external file (Directory) or standalone memory handle (InMemory).
        string? asavDiskPath = kind == StorageKind.Directory
            ? Path.Combine(CreateTempDirectory(), "exported_save.asav")
            : null;
        using var memExport = new InMemoryBufferHandle();

        if (asavDiskPath is not null)
        {
            Assert.That(AsavArchive.TryExportToFile(snapStore, srcId, srcSegStore, asavDiskPath, codecs, out string? exportErr), Is.True, exportErr);
        }
        else
        {
            Assert.That(AsavArchive.TryExport(snapStore, srcId, srcSegStore, memExport, codecs, out string? exportErr), Is.True, exportErr);
        }

        // Open read-only AsavArchiveReader and verify concurrent streaming reads.
        AsavArchiveReader<TestSnapshotMeta> archiveReader;
        if (asavDiskPath is not null)
        {
            Assert.That(AsavArchiveReader<TestSnapshotMeta>.TryOpenFile(asavDiskPath, codecs, out AsavArchiveReader<TestSnapshotMeta>? r, out string? openErr), Is.True, openErr);
            archiveReader = r!;
        }
        else
        {
            Assert.That(AsavArchiveReader<TestSnapshotMeta>.TryOpenHandle(memExport.CreateReadHandle(), ownsHandle: true, codecs, out AsavArchiveReader<TestSnapshotMeta>? r, out string? openErr), Is.True, openErr);
            archiveReader = r!;
        }

        using (archiveReader)
        {
            Assert.That(archiveReader.WorldGuid, Is.EqualTo(srcWorld));
            Assert.That(archiveReader.Meta, Is.EqualTo(srcMeta));
            Assert.That(archiveReader.CoreBytes.Span.SequenceEqual(core), Is.True);
            Assert.That(archiveReader.ThumbnailBytes.Span.SequenceEqual(thumb), Is.True);
            Assert.That(archiveReader.ChunkIndex.Count, Is.EqualTo(expectedChunks.Count));

            Parallel.For(0, 8, _ =>
            {
                var buf = new ArrayBufferWriter<byte>(1024);
                for (int i = 0; i < archiveReader.ChunkIndex.Count; i++)
                {
                    ChunkIndexEntry entry = archiveReader.ChunkIndex[i];
                    Assert.That(entry.SegmentId, Is.EqualTo(0));
                    Assert.That(entry.Codec, Is.EqualTo(ChunkCodecId.Zstd));
                    buf.Clear();
                    Assert.That(archiveReader.TryReadChunk(entry, buf, out string? err), Is.True, err);
                    Assert.That(buf.WrittenSpan.SequenceEqual(expectedChunks[entry.Key]), Is.True);
                }
            });
        }

        // Import into dstWorld with metadata rewrite and verify full fidelity.
        bool importedOk;
        SnapshotMetaInfo<TestSnapshotMeta> importedInfo;
        string? importErr;
        if (asavDiskPath is not null)
        {
            importedOk = AsavArchive.TryImportFromFile(
                asavDiskPath,
                snapStore,
                dstSegStore,
                "Imported_01",
                codecs,
                out importedInfo,
                out importErr,
                rewriteMeta: m => new TestSnapshotMeta(m.PlayerName + "_Imported", m.DayCount + 1, m.PlaySeconds));
        }
        else
        {
            using IRandomAccessHandle readHandle = memExport.CreateReadHandle();
            importedOk = AsavArchive.TryImport(
                readHandle,
                snapStore,
                dstSegStore,
                "Imported_01",
                codecs,
                out importedInfo,
                out importErr,
                rewriteMeta: m => new TestSnapshotMeta(m.PlayerName + "_Imported", m.DayCount + 1, m.PlaySeconds));
        }

        Assert.That(importedOk, Is.True, importErr);
        Assert.That(importedInfo.Meta.PlayerName, Is.EqualTo("Alice_Imported"));
        Assert.That(importedInfo.Meta.DayCount, Is.EqualTo(100));
        Assert.That(importedInfo.HasThumbnail, Is.True);

        var importedId = new SnapshotId(dstWorld, "Imported_01");
        Assert.That(snapStore.TryReadThumbnail(importedId, out byte[]? importedThumb, out _), Is.True);
        Assert.That(importedThumb, Is.EqualTo(thumb));
        Assert.That(snapStore.TryLoadSnapshot(importedId, out SnapshotLoadResult<TestSnapshotMeta>? loadedDst, out _, includeThumbnail: true), Is.True);
        Assert.That(loadedDst!.CoreBytes.Span.SequenceEqual(core), Is.True);
        AssertSnapshotChunksMatch(snapStore, dstSegStore, importedId, expectedChunks, expectedCodec: ChunkCodecId.Zstd);

        // Test 0-chunk world snapshot + ASAV export/import.
        Guid emptyWorld = Guid.NewGuid();
        using var emptySegStore = new ChunkSegmentStore(storage, emptyWorld, codecs);
        var emptySnapId = new SnapshotId(emptyWorld, "Empty_00");
        snapStore.WriteSnapshot(emptySnapId, new TestSnapshotMeta("Empty", 0, 0), Encoding.UTF8.GetBytes("empty-core"), ReadOnlySpan<ChunkIndexEntry>.Empty);

        using var emptyAsavMem = new InMemoryBufferHandle();
        Assert.That(AsavArchive.TryExport(snapStore, emptySnapId, emptySegStore, emptyAsavMem, codecs, out string? emptyExportErr), Is.True, emptyExportErr);
        Guid emptyImportWorld = Guid.NewGuid();
        using var emptyImportSegs = new ChunkSegmentStore(storage, emptyImportWorld, codecs);
        using IRandomAccessHandle emptyRead = emptyAsavMem.CreateReadHandle();
        Assert.That(AsavArchive.TryImport(emptyRead, snapStore, emptyImportSegs, "Empty_Imp", codecs, out _, out string? emptyErr), Is.True, emptyErr);
        Assert.That(snapStore.TryLoadSnapshot(new SnapshotId(emptyImportWorld, "Empty_Imp"), out SnapshotLoadResult<TestSnapshotMeta>? emptyLoaded, out _), Is.True);
        Assert.That(emptyLoaded!.ChunkIndex, Is.Empty);
        Assert.That(Encoding.UTF8.GetString(emptyLoaded.CoreBytes.Span), Is.EqualTo("empty-core"));
    }

    [Test]
    public void IChunkReader_ReadsUniformlyFromSegmentStoreAndAsavArchive()
    {
        var storage = new InMemoryRandomAccessStorage();
        Guid worldGuid = Guid.NewGuid();
        using var codecs = new ChunkCodecSet();
        using var segStore = new ChunkSegmentStore(storage, worldGuid, codecs);
        var snapStore = new SnapshotStore<TestSnapshotMeta>(storage, codecs);

        var key = new ChunkKey(4, -7, 9);
        byte[] expected = CreateRepetitivePayload(1024, seed: 71);
        ChunkIndexEntry[] entries = segStore.AppendBatch([new ChunkWriteInput(key, expected)], ChunkCodecId.Lz4, flushToDisk: false);

        var snapId = new SnapshotId(worldGuid, "UniformRead");
        snapStore.WriteSnapshot(snapId, new TestSnapshotMeta("Uniform", 3, 12.5), Encoding.UTF8.GetBytes("core"), entries, flushToDisk: false);

        using var asavBuf = new InMemoryBufferHandle();
        Assert.That(AsavArchive.TryExport(snapStore, snapId, segStore, asavBuf, codecs, out string? exportErr, flushToDisk: false), Is.True, exportErr);
        using IRandomAccessHandle readHandle = asavBuf.CreateReadHandle();
        Assert.That(AsavArchiveReader<TestSnapshotMeta>.TryOpenHandle(readHandle, ownsHandle: false, codecs, out AsavArchiveReader<TestSnapshotMeta>? asavReader, out string? openErr), Is.True, openErr);

        using (AsavArchiveReader<TestSnapshotMeta> opened = asavReader!)
        {
            (IChunkReader Reader, ChunkIndexEntry Entry)[] sources =
            [
                (segStore, entries[0]),
                (opened, opened.ChunkIndex[0])
            ];

            var scratch = new ArrayBufferWriter<byte>(expected.Length);
            for (int i = 0; i < sources.Length; i++)
            {
                scratch.Clear();
                Assert.That(sources[i].Reader.TryReadChunk(sources[i].Entry, scratch, out string? readErr), Is.True, readErr);
                Assert.That(scratch.WrittenSpan.SequenceEqual(expected), Is.True);
            }
        }
    }

    [TestCase(StorageKind.InMemory)]
    [TestCase(StorageKind.Directory)]
    public void SnapshotStore_EnumerateAndReadMeta_SkipsCorruptSnapshots(StorageKind kind)
    {
        IRandomAccessStorage storage = CreateStorage(kind);
        using var codecs = new ChunkCodecSet();
        var snapStore = new SnapshotStore<TestSnapshotMeta>(storage, codecs);

        Guid worldA = Guid.NewGuid();
        Guid worldB = Guid.NewGuid();
        snapStore.WriteSnapshot(new SnapshotId(worldA, "Slot_1"), new TestSnapshotMeta("P1", 3, 12.0), [1, 2], ReadOnlySpan<ChunkIndexEntry>.Empty);
        snapStore.WriteSnapshot(new SnapshotId(worldB, "Slot_2"), new TestSnapshotMeta("P2", 7, 34.0), [3, 4], ReadOnlySpan<ChunkIndexEntry>.Empty, [9, 8, 7]);
        storage.WriteAtomic(StreamingSavePaths.SnapshotFilePath(worldA, "Corrupt"), Encoding.ASCII.GetBytes("NOT_A_SNAPSHOT"), flushToDisk: false);

        var corruptWarnings = new List<string>();
        IReadOnlyList<SnapshotMetaInfo<TestSnapshotMeta>> all = snapStore.EnumerateSnapshots(worldGuid: null, msg => corruptWarnings.Add(msg));
        Assert.That(all.Count, Is.EqualTo(2));
        Assert.That(corruptWarnings.Count, Is.EqualTo(1));

        IReadOnlyList<SnapshotMetaInfo<TestSnapshotMeta>> onlyB = snapStore.EnumerateSnapshots(worldB);
        Assert.That(onlyB.Count, Is.EqualTo(1));
        Assert.That(onlyB[0].Id, Is.EqualTo(new SnapshotId(worldB, "Slot_2")));
        Assert.That(onlyB[0].HasThumbnail, Is.True);
        Assert.That(onlyB[0].Meta, Is.EqualTo(new TestSnapshotMeta("P2", 7, 34.0)));
    }

    private static bool TryReadWithSpillFallback(
        SpillStore spill,
        ChunkKey key,
        IReadOnlyDictionary<ChunkKey, ChunkIndexEntry> baseline,
        ChunkSegmentStore segStore,
        IBufferWriter<byte> destination,
        out string? failureReason)
    {
        ChunkReadStatus status = spill.TryReadChunk(key, destination, out _, out failureReason);
        if (status == ChunkReadStatus.Success)
            return true;
        if (status == ChunkReadStatus.Failed)
            return false;

        if (!baseline.TryGetValue(key, out ChunkIndexEntry baseEntry))
        {
            failureReason = $"Chunk {key} missing from baseline.";
            return false;
        }

        return segStore.TryReadChunk(baseEntry, destination, out failureReason);
    }

    private static void AssertSnapshotChunksMatch(
        SnapshotStore<TestSnapshotMeta> snapStore,
        ChunkSegmentStore segStore,
        SnapshotId id,
        IReadOnlyDictionary<ChunkKey, byte[]> expectedData,
        ChunkCodecId? expectedCodec = null)
    {
        Assert.That(snapStore.TryLoadSnapshot(id, out SnapshotLoadResult<TestSnapshotMeta>? loaded, out string? loadErr), Is.True, loadErr);
        Assert.That(loaded!.ChunkIndex.Count, Is.EqualTo(expectedData.Count));

        var readerBuf = new ArrayBufferWriter<byte>(2048);
        for (int i = 0; i < loaded.ChunkIndex.Count; i++)
        {
            ChunkIndexEntry entry = loaded.ChunkIndex[i];
            if (expectedCodec.HasValue)
                Assert.That(entry.Codec, Is.EqualTo(expectedCodec.Value));

            readerBuf.Clear();
            Assert.That(segStore.TryReadChunk(entry, readerBuf, out string? readErr), Is.True, readErr);
            Assert.That(readerBuf.WrittenSpan.SequenceEqual(expectedData[entry.Key]), Is.True);
        }
    }

    private IRandomAccessStorage CreateStorage(StorageKind kind) =>
        kind == StorageKind.InMemory
            ? new InMemoryRandomAccessStorage()
            : new DirectoryRandomAccessStorage(CreateTempDirectory());

    private string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"AlcoIoTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private static byte[] CreateRepetitivePayload(int length, int seed)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++)
            data[i] = (byte)((i / 8 + seed) & 0xFF);
        return data;
    }

    private static byte[] CreateRandomPayload(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private sealed class InterceptingAppendStorage(
        IRandomAccessStorage inner,
        Action<string> onOpenAppendOrCreate) : IRandomAccessStorage
    {
        public bool FileExists(string relativePath) => inner.FileExists(relativePath);

        public bool TryOpenRead(string relativePath, [NotNullWhen(true)] out IRandomAccessHandle? handle) =>
            inner.TryOpenRead(relativePath, out handle);

        public IRandomAccessHandle OpenAppendOrCreate(string relativePath)
        {
            onOpenAppendOrCreate(relativePath);
            return inner.OpenAppendOrCreate(relativePath);
        }

        public void WriteAtomic(string relativePath, ReadOnlySpan<byte> data, bool flushToDisk = true) =>
            inner.WriteAtomic(relativePath, data, flushToDisk);

        public bool TryReadAllBytes(string relativePath, [NotNullWhen(true)] out byte[]? data) =>
            inner.TryReadAllBytes(relativePath, out data);

        public IReadOnlyList<string> EnumerateFiles(string relativeDirectory, string searchPattern, bool recursive = false) =>
            inner.EnumerateFiles(relativeDirectory, searchPattern, recursive);

        public IReadOnlyList<string> EnumerateDirectories(string relativeDirectory) =>
            inner.EnumerateDirectories(relativeDirectory);

        public bool DeleteFile(string relativePath) => inner.DeleteFile(relativePath);

        public void DeleteDirectoryIfEmpty(string relativeDirectory) =>
            inner.DeleteDirectoryIfEmpty(relativeDirectory);
    }

    private sealed class TestSnapshotMeta : ISerializable, IEquatable<TestSnapshotMeta>
    {
        private string _playerName = string.Empty;
        private int _dayCount;
        private double _playSeconds;

        public TestSnapshotMeta()
        {
        }

        public TestSnapshotMeta(string playerName, int dayCount, double playSeconds)
        {
            _playerName = playerName;
            _dayCount = dayCount;
            _playSeconds = playSeconds;
        }

        public string PlayerName => _playerName;
        public int DayCount => _dayCount;
        public double PlaySeconds => _playSeconds;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindString(nameof(_playerName), ref _playerName);
            node.BindValue(nameof(_dayCount), ref _dayCount);
            node.BindValue(nameof(_playSeconds), ref _playSeconds);
        }

        public bool Equals(TestSnapshotMeta? other) =>
            other is not null &&
            string.Equals(_playerName, other._playerName, StringComparison.Ordinal) &&
            _dayCount == other._dayCount &&
            _playSeconds.Equals(other._playSeconds);

        public override bool Equals(object? obj) => Equals(obj as TestSnapshotMeta);

        public override int GetHashCode() => HashCode.Combine(_playerName, _dayCount, _playSeconds);
    }
}
