using System.IO;
using System.Text;
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

    public static ReadOnlySpan<byte> Magic => s_magic;

    private string _name = "";
    private int _value;

    public string Name => _name;
    public int Value => _value;

    public TestStructuredFileMeta() { }

    public TestStructuredFileMeta(string name, int value)
    {
        _name = name;
        _value = value;
    }

    public void OnSerialize(SerializeNode node, SerializeMode mode)
    {
        node.BindString(nameof(_name), ref _name);
        node.BindValue(nameof(_value), ref _value);
    }
}

[TestFixture]
public class TestStructuredFileBase
{
    [Test]
    public void TestCompose_CorrectLayout()
    {
        var meta = new TestStructuredFileMeta("hello", 42);
        byte[] content = { 0x01, 0x02, 0x03, 0x04 };

        byte[] data = StructuredFileUtility.Compose(meta, content);

        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        byte[] magic = reader.ReadBytes(4);
        Assert.That(Encoding.ASCII.GetString(magic), Is.EqualTo("test"));

        int metaLength = reader.ReadInt32();
        Assert.That(metaLength, Is.GreaterThan(0));
        byte[] metaBytes = reader.ReadBytes(metaLength);
        TestStructuredFileMeta readMeta = BinaryParser.Decode<TestStructuredFileMeta>(metaBytes);
        Assert.That(readMeta.Name, Is.EqualTo("hello"));
        Assert.That(readMeta.Value, Is.EqualTo(42));

        int contentLength = reader.ReadInt32();
        Assert.That(contentLength, Is.EqualTo(4));
        byte[] readContent = reader.ReadBytes(contentLength);
        Assert.That(readContent, Is.EqualTo(new byte[] { 0x01, 0x02, 0x03, 0x04 }));
    }

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

    [Test]
    public void TestReadMeta_ReturnsOnlyMeta()
    {
        var meta = new TestStructuredFileMeta("meta", 7);
        byte[] content = { 0xAA, 0xBB };
        byte[] data = StructuredFileUtility.Compose(meta, content);

        TestStructuredFileMeta readMeta = StructuredFileUtility.ReadMeta<TestStructuredFileMeta>(data);

        Assert.That(readMeta.Name, Is.EqualTo("meta"));
        Assert.That(readMeta.Value, Is.EqualTo(7));
    }

    [Test]
    public void TestRead_InvalidMagic_Throws()
    {
        byte[] badData = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0xFF };

        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.Read<TestStructuredFileMeta>(badData));
    }

    [Test]
    public void TestRead_TooShort_Throws()
    {
        byte[] shortData = "test"u8.ToArray(); // only 4 bytes

        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.Read<TestStructuredFileMeta>(shortData));
    }

    [Test]
    public void TestRead_NegativeMetaLength_Throws()
    {
        var meta = new TestStructuredFileMeta("x", 1);
        byte[] content = { 0x01 };
        byte[] data = StructuredFileUtility.Compose(meta, content);

        // Corrupt metaLength to -1 (offset 4, little-endian)
        data[4] = 0xFF;
        data[5] = 0xFF;
        data[6] = 0xFF;
        data[7] = 0xFF;

        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.Read<TestStructuredFileMeta>(data));
    }

    [Test]
    public void TestReadMeta_Stream_ReturnsOnlyMeta()
    {
        var meta = new TestStructuredFileMeta("stream", 3);
        byte[] content = { 0x01, 0x02, 0x03 };
        byte[] data = StructuredFileUtility.Compose(meta, content);

        using var ms = new MemoryStream(data);
        TestStructuredFileMeta readMeta = StructuredFileUtility.ReadMeta<TestStructuredFileMeta>(ms);

        Assert.That(readMeta.Name, Is.EqualTo("stream"));
        Assert.That(readMeta.Value, Is.EqualTo(3));
    }

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

    [Test]
    public void TestRead_NegativeContentLength_Throws()
    {
        var meta = new TestStructuredFileMeta("x", 1);
        byte[] content = { 0x01 };
        byte[] data = StructuredFileUtility.Compose(meta, content);

        // Corrupt contentLength to -1 (offset = 8 + metaLength)
        int metaLength = BitConverter.ToInt32(data.AsSpan(4, 4));
        int contentLengthOffset = 8 + metaLength;
        data[contentLengthOffset] = 0xFF;
        data[contentLengthOffset + 1] = 0xFF;
        data[contentLengthOffset + 2] = 0xFF;
        data[contentLengthOffset + 3] = 0xFF;

        Assert.Throws<InvalidDataException>(() => StructuredFileUtility.Read<TestStructuredFileMeta>(data));
    }
}
