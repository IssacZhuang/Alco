using System.Text.Json;
using System.Xml;
using BenchmarkDotNet.Attributes;

namespace Alco.Benchmark;

public class BenchmarkSerializationEncoding
{

    [GlobalSetup]
    public void Setup()
    {
    }

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
    [Benchmark(Description = "BinaryTable encoding")]
    public void BinaryTableEncoding(){
        BinaryTable table = new BinaryTable();
        table["intVal"] = 1;
        table["strVal"] = "2";
        table["floatVal"] = 3.14;
        table["boolVal"] = true;
        byte[] bytes = BinaryParser.EncodeTable(table);
    }
}

public class BenchmarkSerializationDecoding
{
    private string _xmlInput;
    private string _jsonInput;
    private byte[] _binaryInput;

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


        BinaryTable table = new BinaryTable();
        table["intVal"] = 1;
        table["strVal"] = "2";
        table["floatVal"] = 3.14;
        table["boolVal"] = true;
        _binaryInput = BinaryParser.EncodeTable(table);
    }

    [Benchmark(Description = "XmlDocument decoding")]
    public void XmlDocumentDecoding()
    {
        XmlDocument doc = new XmlDocument();
        doc.LoadXml(_xmlInput);
    }

    [Benchmark(Description = "JsonDocument decoding")]
    public void JsonDocumentDecoding()
    {
        JsonDocument doc = JsonDocument.Parse(_jsonInput);
    }

    [Benchmark(Description = "BinaryTable decoding")]
    public void BinaryTableDecoding()
    {
        BinaryTable table = BinaryParser.DecodeTable(_binaryInput);
    }
}
