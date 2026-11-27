using System;
using System.Collections.Generic;
using System.IO;

namespace Alco.Test;

/// <summary>Tests the typed BinaryParser facade and its load contracts.</summary>
public class TestBinaryParser
{
    /// <summary>Tests span decoding of scalars, escaped keys, strings, and nested objects.</summary>
    [Test]
    public void TestDecodeFromSpan()
    {
        var source = new Payload
        {
            Text = "value with\ttabs,\nnewlines, and \u96EA",
            Integer = 12345,
            Float = 123.456f,
            Boolean = true,
            Values = { "first", "", null!, "last" },
            Child = new Payload { Text = null!, Integer = 789 },
        };
        ReadOnlyMemory<byte> bytes = BinaryParser.Encode(source, static error => Assert.Fail(error));

        Payload decoded = BinaryParser.Decode<Payload>(bytes.Span, onError: static error => Assert.Fail(error));

        Assert.That(decoded.Text, Is.EqualTo(source.Text));
        Assert.That(decoded.Integer, Is.EqualTo(12345));
        Assert.That(decoded.Float, Is.EqualTo(123.456f));
        Assert.That(decoded.Boolean, Is.True);
        Assert.That(decoded.Values, Is.EqualTo(new[] { "first", "", "", "last" }));
        Assert.That(decoded.Child?.Text, Is.Empty);
        Assert.That(decoded.Child?.Integer, Is.EqualTo(789));
        Assert.That(decoded.Child?.Child, Is.Null);
    }

    /// <summary>Tests that the facade encodes PackedBinary without eliding default bindings.</summary>
    [Test]
    public void TestEncodePreservesDefaultBindings()
    {
        var source = new Payload();

        ReadOnlyMemory<byte> bytes = BinaryParser.Encode(source);

        Assert.That(bytes.Span[..4].SequenceEqual("APKB"u8), Is.True);
        Assert.That(bytes.ToArray(), Is.EqualTo(PackedBinarySerializer.Encode(source, elideDefaults: false)));
    }

    /// <summary>Tests that Decode visits the root and child for load and post-load, even without a reference context.</summary>
    [Test]
    public void TestPostLoad_Decode_New()
    {
        byte[] bytes = BinaryParser.Encode(new Payload { Child = new Payload() }).ToArray();

        Payload decoded = BinaryParser.Decode<Payload>(bytes, onError: static error => Assert.Fail(error));

        AssertLoadPasses(decoded, postLoads: 1);
    }

    /// <summary>Tests that factory pre-reads do not consume fields needed during load or post-load.</summary>
    [Test]
    public void TestPostLoad_Decode_Factory()
    {
        var source = new Payload { Text = "root", Child = new Payload { Text = "child" } };
        ReadOnlyMemory<byte> bytes = BinaryParser.Encode(source);

        Payload decoded = BinaryParser.Decode(bytes, static node =>
        {
            Assert.That(node.TryGetChildNode("child", out SerializeReadNode? child), Is.True);
            Assert.That(child!.GetString(Payload.TextKey), Is.EqualTo("child"));
            Assert.That(node.GetString(Payload.TextKey), Is.EqualTo("root"));
            return new Payload();
        }, static error => Assert.Fail(error), new ReferenceContext());

        Assert.That(decoded.Text, Is.EqualTo("root"));
        Assert.That(decoded.Child?.Text, Is.EqualTo("child"));
        AssertLoadPasses(decoded, postLoads: 1);
    }

    /// <summary>Tests that Populate reuses objects and runs post-load only with a reference context.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void TestPostLoad_Populate(bool withReferenceContext)
    {
        ReadOnlyMemory<byte> bytes = BinaryParser.Encode(new Payload { Child = new Payload { Text = "child" } });
        var existingChild = new Payload();
        var target = new Payload { Child = existingChild };

        BinaryParser.Populate(bytes, target, static error => Assert.Fail(error), withReferenceContext ? new ReferenceContext() : null);

        Assert.That(target.Child, Is.SameAs(existingChild));
        Assert.That(existingChild.Text, Is.EqualTo("child"));
        AssertLoadPasses(target, postLoads: withReferenceContext ? 1 : 0);
    }

    /// <summary>Tests that the facade reports and rejects a legacy empty table.</summary>
    [Test]
    public void TestDecodeRejectsLegacyPayload()
    {
        byte[] legacy = new byte[sizeof(int)];
        var errors = new List<string>();

        Assert.Throws<InvalidDataException>(() => BinaryParser.Decode<Payload>(legacy, errors.Add));
        Assert.That(errors, Has.Count.EqualTo(1));
    }

    private static void AssertLoadPasses(Payload result, int postLoads)
    {
        Assert.That(result.Loads, Is.EqualTo(1));
        Assert.That(result.PostLoads, Is.EqualTo(postLoads));
        Assert.That(result.Child?.Loads, Is.EqualTo(1));
        Assert.That(result.Child?.PostLoads, Is.EqualTo(postLoads));
    }

    private sealed class Payload : ISerializable
    {
        public const string TextKey = "ke\ty\n";
        public string Text = "";
        public int Integer;
        public float Float;
        public bool Boolean;
        public Payload? Child;

        /// <summary>Gets the strings to serialize.</summary>
        public List<string> Values { get; } = new();

        /// <summary>Gets the number of load passes.</summary>
        public int Loads { get; private set; }

        /// <summary>Gets the number of post-load passes.</summary>
        public int PostLoads { get; private set; }

        /// <inheritdoc/>
        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindString(TextKey, ref Text);
            node.BindValue("int", ref Integer);
            node.BindValue("float", ref Float);
            node.BindValue("bool", ref Boolean);
            node.BindCollection("values", Values);
            node.BindSerializableOptional("child", ref Child, static _ => new Payload());
            if (mode == SerializeMode.Load)
            {
                Loads++;
            }
            else if (mode == SerializeMode.PostLoad)
            {
                PostLoads++;
            }
        }
    }
}
