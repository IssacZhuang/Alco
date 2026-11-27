using System.Buffers.Binary;
using System.IO;
using Alco;
using Alco.IO;
using NUnit.Framework;

namespace Alco.IO.Test;

/// <summary>
/// Test meta type for StructuredFile tests.
/// </summary>
public sealed class TestStructuredFileMeta : IStructuredFileMeta
{
    private static readonly byte[] s_magic = "test"u8.ToArray();

    /// <summary>
    /// Gets the magic bytes identifying this test meta type.
    /// </summary>
    public static ReadOnlySpan<byte> Magic => s_magic;

    private string _name = "";
    private int _value;

    /// <summary>
    /// Gets the stored test name.
    /// </summary>
    public string Name => _name;
    /// <summary>
    /// Gets the stored test value.
    /// </summary>
    public int Value => _value;

    /// <summary>
    /// Creates an empty test meta instance.
    /// </summary>
    public TestStructuredFileMeta() { }

    /// <summary>
    /// Creates a test meta instance with the given name and value.
    /// </summary>
    public TestStructuredFileMeta(string name, int value)
    {
        _name = name;
        _value = value;
    }

    /// <summary>
    /// Serializes the test meta fields into the node.
    /// </summary>
    public void OnSerialize(SerializeNode node, SerializeMode mode)
    {
        node.BindString(nameof(_name), ref _name);
        node.BindValue(nameof(_value), ref _value);
    }
}

/// <summary>
/// Tests composing, reading, and writing structured files with metadata and content.
/// </summary>
[TestFixture]
public class TestStructuredFileBase
{
    /// <summary>
    /// Tests that composed data has the expected magic, metadata, and content layout.
    /// </summary>
    [Test]
    public void TestCompose_CorrectLayout()
    {
        var meta = new TestStructuredFileMeta("hello", 42);
        byte[] content = { 0x01, 0x02, 0x03, 0x04 };

        byte[] data = StructuredFileUtility.Compose(meta, content);

        Assert.That(data.AsSpan(0, 4).SequenceEqual("test"u8), Is.True);
        int metaLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        Assert.That(data.AsSpan(8, 4).SequenceEqual("APKB"u8), Is.True);
        ReadOnlyMemory<byte> metaBytes = data.AsMemory(8, metaLength);
        Assert.That(metaBytes.ToArray(), Is.EqualTo(PackedBinarySerializer.Encode(meta, elideDefaults: false)));

        TestStructuredFileMeta readMeta = PackedBinarySerializer.Decode<TestStructuredFileMeta>(metaBytes);
        Assert.That(readMeta.Name, Is.EqualTo("hello"));
        Assert.That(readMeta.Value, Is.EqualTo(42));
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8 + metaLength)), Is.EqualTo(content.Length));
        Assert.That(data.AsSpan(12 + metaLength).ToArray(), Is.EqualTo(content));
    }

    /// <summary>
    /// Tests that reading returns both metadata and content.
    /// </summary>
    [Test]
    public void TestRead_ReturnsMetaAndContent()
    {
        var meta = new TestStructuredFileMeta("world", 99);
        byte[] content = { 0xDE, 0xAD, 0xBE, 0xEF };
        byte[] data = StructuredFileUtility.Compose(meta, content);

        var (readMeta, readContent) = StructuredFileUtility.Read<TestStructuredFileMeta>(data);

        Assert.That(readMeta.Name, Is.EqualTo("world"));
        Assert.That(readMeta.Value, Is.EqualTo(99));
        Assert.That(readContent.Length, Is.EqualTo(4));
        Assert.That(readContent.ToArray(), Is.EqualTo(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }));
    }

    /// <summary>
    /// Tests that reading metadata alone returns the stored metadata.
    /// </summary>
    [Test]
    public void TestReadMeta_ReturnsOnlyMeta()
    {
        var meta = new TestStructuredFileMeta("meta", 7);
        byte[] content = { 0xAA, 0xBB };
        byte[] data = StructuredFileUtility.Compose(meta, content);

        int metaLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        ReadOnlyMemory<byte> headerAndMeta = data.AsMemory(0, 8 + metaLength);
        TestStructuredFileMeta readMeta = StructuredFileUtility.ReadMeta<TestStructuredFileMeta>(headerAndMeta);

        Assert.That(readMeta.Name, Is.EqualTo("meta"));
        Assert.That(readMeta.Value, Is.EqualTo(7));
        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.Read<TestStructuredFileMeta>(headerAndMeta));
    }

    /// <summary>
    /// Tests that data with an invalid magic throws.
    /// </summary>
    [Test]
    public void TestRead_InvalidMagic_Throws()
    {
        byte[] badData = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0xFF };

        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.Read<TestStructuredFileMeta>(badData));
    }

    /// <summary>
    /// Tests that data shorter than the header throws.
    /// </summary>
    [Test]
    public void TestRead_TooShort_Throws()
    {
        byte[] shortData = "test"u8.ToArray();

        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.Read<TestStructuredFileMeta>(shortData));
        using var stream = new FragmentedReadStream(shortData);
        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.ReadMeta<TestStructuredFileMeta>(stream));
    }

    /// <summary>Tests that negative and oversized metadata lengths are rejected before large allocations.</summary>
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    [TestCase(int.MaxValue - 4096)]
    public void TestRead_InvalidMetaLength_Throws(int metaLength)
    {
        byte[] data = StructuredFileUtility.Compose(new TestStructuredFileMeta("x", 1), [0x01]);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), metaLength);

        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.Read<TestStructuredFileMeta>(data));
        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.ReadMeta<TestStructuredFileMeta>(data));
        using var seekable = new MemoryStream(data);
        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.ReadMeta<TestStructuredFileMeta>(seekable));
        using var fragmented = new FragmentedReadStream(data);
        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.ReadMeta<TestStructuredFileMeta>(fragmented));
    }

    /// <summary>Tests that a valid envelope does not make legacy or non-APKB metadata readable.</summary>
    [TestCase("1400000000050000005F6E616D65060000006C6567616379", TestName = "TestRead_RejectsLegacyMetadata")]
    [TestCase("4E4F504501000000110000000000000000", TestName = "TestRead_RejectsNonApkbMetadata")]
    public void TestRead_RejectsNonPackedMetadata(string metadataHex)
    {
        byte[] meta = Convert.FromHexString(metadataHex);
        byte[] data = new byte[12 + meta.Length];
        "test"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), meta.Length);
        meta.CopyTo(data, 8);

        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.Read<TestStructuredFileMeta>(data));
        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.ReadMeta<TestStructuredFileMeta>(data));
        using var stream = new MemoryStream(data);
        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.ReadMeta<TestStructuredFileMeta>(stream));
    }

    /// <summary>Tests that fragmented metadata reads stop before the content length field.</summary>
    [TestCase(6)]
    [TestCase(5000)]
    public void TestReadMeta_Stream_ReturnsOnlyMeta(int nameLength)
    {
        string name = new('x', nameLength);
        var meta = new TestStructuredFileMeta(name, 3);
        byte[] content = [0x01, 0x02, 0x03];
        byte[] data = StructuredFileUtility.Compose(meta, content);
        using var stream = new FragmentedReadStream([0xCC, .. data]);
        Assert.That(stream.ReadByte(), Is.EqualTo(0xCC));

        TestStructuredFileMeta readMeta = StructuredFileUtility.ReadMeta<TestStructuredFileMeta>(stream);

        Assert.That(readMeta.Name, Is.EqualTo(name));
        Assert.That(readMeta.Value, Is.EqualTo(3));
        Span<byte> length = stackalloc byte[sizeof(int)];
        stream.ReadExactly(length);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(length), Is.EqualTo(content.Length));
        byte[] readContent = new byte[content.Length];
        stream.ReadExactly(readContent);
        Assert.That(readContent, Is.EqualTo(content));
    }

    /// <summary>
    /// Tests that writing to a stream produces the composed bytes.
    /// </summary>
    [Test]
    public void TestWriteTo_CorrectBytesInStream()
    {
        var meta = new TestStructuredFileMeta("writeto", 10);
        byte[] content = { 0xCA, 0xFE };
        byte[] expected = StructuredFileUtility.Compose(meta, content);

        using var ms = new MemoryStream();
        StructuredFileUtility.WriteTo(ms, meta, content);

        Assert.That(ms.ToArray(), Is.EqualTo(expected));
    }

    /// <summary>
    /// Tests that reading from a stream returns metadata and content.
    /// </summary>
    [Test]
    public void TestRead_Stream_ReturnsMetaAndContent()
    {
        var meta = new TestStructuredFileMeta("streamread", 22);
        byte[] content = { 0x01, 0x02 };
        byte[] data = StructuredFileUtility.Compose(meta, content);

        using var ms = new MemoryStream(data);
        var (readMeta, readContent) = StructuredFileUtility.Read<TestStructuredFileMeta>(ms);

        Assert.That(readMeta.Name, Is.EqualTo("streamread"));
        Assert.That(readContent.ToArray(), Is.EqualTo(new byte[] { 0x01, 0x02 }));
    }

    /// <summary>Tests that negative and oversized content lengths are rejected.</summary>
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void TestRead_InvalidContentLength_Throws(int contentLength)
    {
        byte[] data = StructuredFileUtility.Compose(new TestStructuredFileMeta("x", 1), [0x01]);
        int metaLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8 + metaLength), contentLength);

        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.Read<TestStructuredFileMeta>(data));
    }

    private sealed class FragmentedReadStream(byte[] data) : Stream
    {
        private int _offset;

        /// <inheritdoc/>
        public override bool CanRead => true;
        /// <inheritdoc/>
        public override bool CanSeek => false;
        /// <inheritdoc/>
        public override bool CanWrite => false;
        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            int count = Math.Min(Math.Min(3, buffer.Length), data.Length - _offset);
            data.AsSpan(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        /// <inheritdoc/>
        public override void Flush() => throw new NotSupportedException();
        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();
        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
