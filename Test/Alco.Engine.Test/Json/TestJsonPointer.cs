using System.Text.Json.Nodes;
using NUnit.Framework;
using Alco.Engine;

namespace Alco.Engine.Test;

[TestFixture]
public class TestJsonPointer
{
    private const string SampleJson = """
    {
      "store": {
        "book": [
          { "title": "A", "price": 5 },
          { "title": "B", "price": 15 }
        ],
        "name": "Shop"
      },
      "a/b": 1,
      "c~d": 2
    }
    """;

    [Test]
    public void RootPointer_ReturnsRoot()
    {
        var node = JsonNode.Parse(SampleJson)!;

        Assert.That(new JsonPointer("").Find(node), Is.SameAs(node));
        Assert.That(new JsonPointer("/").Find(node), Is.SameAs(node));
    }

    [Test]
    public void ObjectProperty_Path_Works()
    {
        var node = JsonNode.Parse(SampleJson)!;
        var ptr = new JsonPointer("/store/name");

        var result = ptr.Find(node);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.GetValue<string>(), Is.EqualTo("Shop"));
    }

    [Test]
    public void ArrayIndex_Path_Works()
    {
        var node = JsonNode.Parse(SampleJson)!;
        var ptr = new JsonPointer("/store/book/0/title");

        var result = ptr.Find(node);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.GetValue<string>(), Is.EqualTo("A"));
    }

    [Test]
    /// <summary>
    /// Verifies that object property paths work identically without a leading '/'.
    /// </summary>
    public void ObjectProperty_Path_WithoutLeadingSlash_Works()
    {
        var node = JsonNode.Parse(SampleJson)!;
        var ptrNoSlash = new JsonPointer("store/name");

        var result = ptrNoSlash.Find(node);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.GetValue<string>(), Is.EqualTo("Shop"));
    }

    [Test]
    /// <summary>
    /// Verifies that array index paths work identically without a leading '/'.
    /// </summary>
    public void ArrayIndex_Path_WithoutLeadingSlash_Works()
    {
        var node = JsonNode.Parse(SampleJson)!;
        var ptrNoSlash = new JsonPointer("store/book/0/title");

        var result = ptrNoSlash.Find(node);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.GetValue<string>(), Is.EqualTo("A"));
    }

    [Test]
    public void Decoding_TildeAndUriPercent_Works()
    {
        var node = JsonNode.Parse(SampleJson)!;

        // ~1 => '/', ~0 => '~'
        var ptr1 = new JsonPointer("/a~1b");
        var val1 = ptr1.Find(node);
        Assert.That(val1, Is.Not.Null);
        Assert.That(val1!.GetValue<int>(), Is.EqualTo(1));

        var ptr2 = new JsonPointer("/c~0d");
        var val2 = ptr2.Find(node);
        Assert.That(val2, Is.Not.Null);
        Assert.That(val2!.GetValue<int>(), Is.EqualTo(2));

        // Percent-decoding (space)
        var node2 = JsonNode.Parse("{ \"a b\": 3 }")!;
        var ptr3 = new JsonPointer("/a%20b");
        var val3 = ptr3.Find(node2);
        Assert.That(val3, Is.Not.Null);
        Assert.That(val3!.GetValue<int>(), Is.EqualTo(3));
    }

    [Test]
    public void MissingProperty_ReturnsNull()
    {
        var node = JsonNode.Parse(SampleJson)!;
        var ptr = new JsonPointer("/store/unknown");

        Assert.That(ptr.Find(node), Is.Null);
    }

    [Test]
    public void InvalidArrayIndex_ReturnsNull()
    {
        var node = JsonNode.Parse(SampleJson)!;
        var ptr = new JsonPointer("/store/book/5/title");

        Assert.That(ptr.Find(node), Is.Null);
    }

    [Test]
    public void ParentPointer_Works()
    {
        var ptr = new JsonPointer("/store/book/0/title");

        Assert.That(ptr.ParentPointer, Is.Not.Null);
        Assert.That(ptr.ParentPointer!.ToString(), Is.EqualTo("/store/book/0"));
        Assert.That(ptr.ParentPointer!.ParentPointer!.ToString(), Is.EqualTo("/store/book"));
        Assert.That(ptr.ParentPointer!.ParentPointer!.ParentPointer!.ToString(), Is.EqualTo("/store"));
        Assert.That(ptr.ParentPointer!.ParentPointer!.ParentPointer!.ParentPointer, Is.Not.Null);
        Assert.That(ptr.ParentPointer!.ParentPointer!.ParentPointer!.ParentPointer!.ToString(), Is.EqualTo("/"));
        Assert.That(ptr.ParentPointer!.ParentPointer!.ParentPointer!.ParentPointer!.ParentPointer, Is.Null);
        
    }
}

