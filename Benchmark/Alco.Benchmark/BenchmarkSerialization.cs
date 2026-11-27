using System.Text.Json;
using System.Xml;
using BenchmarkDotNet.Attributes;

namespace Alco.Benchmark;

/// <summary>
/// Benchmarks encoding a small object with XmlDocument, System.Text.Json, and PackedBinary.
/// </summary>
public class BenchmarkSerializationEncoding
{
    /// <summary>
    /// Benchmarks encoding an object with XmlDocument.
    /// </summary>
    [Benchmark(Description = "XmlDocument encoding")]
    public void XmlDocumentEncoding()
    {
        XmlDocument doc = new XmlDocument();
        XmlElement root = doc.CreateElement("root");
        doc.AppendChild(root);
        XmlElement element = doc.CreateElement("intVal");
        element.InnerText = 1.ToString();
        root.AppendChild(element);
        element = doc.CreateElement("strVal");
        element.InnerText = "2";
        root.AppendChild(element);
        element = doc.CreateElement("floatVal");
        element.InnerText = 3.14.ToString();
        root.AppendChild(element);
        element = doc.CreateElement("boolVal");
        element.InnerText = true.ToString();
        root.AppendChild(element);
        string xml = doc.OuterXml;
    }

    /// <summary>
    /// Benchmarks encoding an object with System.Text.Json.
    /// </summary>
    [Benchmark(Description = "JsonDocument encoding")]
    public void JsonDocumentEncoding()
    {
        var json = JsonSerializer.Serialize(new
        {
            root = new
            {
                intVal = 1,
                strVal = "2",
                floatVal = 3.14,
                boolVal = true
            }
        });

    }

    /// <summary>Benchmarks encoding an object with PackedBinary.</summary>
    [Benchmark(Description = "PackedBinary encoding")]
    public byte[] PackedBinaryEncoding()
    {
        return PackedBinarySerializer.Encode(new SerializationSample(), elideDefaults: false);
    }
}

/// <summary>
/// Benchmarks decoding a small object with XmlDocument, System.Text.Json, and PackedBinary.
/// </summary>
public class BenchmarkSerializationDecoding
{
    private string _xmlInput = string.Empty;
    private string _jsonInput = string.Empty;
    private ReadOnlyMemory<byte> _binaryInput;

    /// <summary>
    /// Builds the XML, JSON, and binary inputs from the same object.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        XmlDocument input = new XmlDocument();
        XmlElement root = input.CreateElement("root");
        input.AppendChild(root); // Add root to document
        XmlElement element = input.CreateElement("intVal");
        element.InnerText = "1";
        root.AppendChild(element);
        element = input.CreateElement("strVal");
        element.InnerText = "2";
        root.AppendChild(element);
        element = input.CreateElement("floatVal");
        element.InnerText = "3.14";
        root.AppendChild(element);
        element = input.CreateElement("boolVal");
        element.InnerText = "true";
        root.AppendChild(element);
        _xmlInput = input.OuterXml;

        _jsonInput = JsonSerializer.Serialize(new
        {
            root = new
            {
                intVal = 1,
                strVal = "2",
                floatVal = 3.14,
                boolVal = true
            }
        });

        _binaryInput = PackedBinarySerializer.Encode(new SerializationSample(), elideDefaults: false);
    }

    /// <summary>
    /// Benchmarks decoding an object with XmlDocument.
    /// </summary>
    [Benchmark(Description = "XmlDocument decoding")]
    public void XmlDocumentDecoding()
    {
        XmlDocument doc = new XmlDocument();
        doc.LoadXml(_xmlInput);
    }

    /// <summary>
    /// Benchmarks decoding an object with System.Text.Json.
    /// </summary>
    [Benchmark(Description = "JsonDocument decoding")]
    public void JsonDocumentDecoding()
    {
        using JsonDocument doc = JsonDocument.Parse(_jsonInput);
    }

    /// <summary>Benchmarks decoding an object with PackedBinary.</summary>
    [Benchmark(Description = "PackedBinary decoding")]
    public ISerializable PackedBinaryDecoding()
    {
        return PackedBinarySerializer.Decode<SerializationSample>(_binaryInput);
    }
}

file sealed class SerializationSample : ISerializable
{
    private int _intVal = 1;
    private string _strVal = "2";
    private double _floatVal = 3.14;
    private bool _boolVal = true;

    void ISerializable.OnSerialize(SerializeNode node, SerializeMode mode)
    {
        node.BindValue("intVal", ref _intVal);
        node.BindString("strVal", ref _strVal);
        node.BindValue("floatVal", ref _floatVal);
        node.BindValue("boolVal", ref _boolVal);
    }
}
