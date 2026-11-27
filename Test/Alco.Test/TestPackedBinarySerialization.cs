using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Alco.Test;

/// <summary>Tests the Phase 1 packed binary wire format and serialization bindings.</summary>
public class TestPackedBinarySerialization
{
    /// <summary>Checks fixed-width scalars, null tags, UTF-8 interning, and appending to a buffer writer.</summary>
    [Test]
    public void TestScalarWireFormat()
    {
        var source = new Scalars
        {
            Byte = 0xA5,
            Code = SignedCode.Negative,
            Int = 0x12345678,
            Long = 0x0102030405060708UL,
            Wide = new UInt128(0x1112131415161718UL, 0x0102030405060708UL),
            Present = 0x01020304,
            Text = "\u96EA",
            Shared = "a",
            Upper = "\u96EA",
        };
        byte[] expected = Convert.FromHexString(
            "41504B4201000100500000000B000000" + // Header: string table at 80, 11 strings.
            "0B" +
            "0001A5" +
            "0102FEFF" +
            "020378563412" +
            "03040807060504030201" +
            "040508070605040302011817161514131211" +
            "0506010404030201" +                // Nullable: present, size 4, little-endian value.
            "060600" +                          // Nullable: not present.
            "070708080700090708" +              // Reuse string IDs across keys and values.
            "0A00" +                            // Explicit null optional table, not a missing field.
            "0161016201630164016501660167016803E99BAA0141017A");

        Assert.That(PackedBinarySerializer.Encode(source, elideDefaults: true), Is.EqualTo(expected));
        var writer = new ArrayBufferWriter<byte>(1);
        writer.GetSpan(1)[0] = 0xFF;
        writer.Advance(1);
        PackedBinarySerializer.EncodeTo(writer, source, elideDefaults: true);
        Assert.That(writer.WrittenSpan[0], Is.EqualTo(0xFF));
        Assert.That(writer.WrittenMemory.Slice(1).ToArray(), Is.EqualTo(expected));

        Scalars result = PackedBinarySerializer.Decode<Scalars>(expected);
        Assert.That(result.Byte, Is.EqualTo(source.Byte));
        Assert.That(result.Code, Is.EqualTo(source.Code));
        Assert.That(result.Int, Is.EqualTo(source.Int));
        Assert.That(result.Long, Is.EqualTo(source.Long));
        Assert.That(result.Wide, Is.EqualTo(source.Wide));
        Assert.That(result.Present, Is.EqualTo(source.Present));
        Assert.That(result.Absent, Is.Null);
        Assert.That(result.Text, Is.EqualTo(source.Text));
        Assert.That(result.Shared, Is.EqualTo(source.Shared));
        Assert.That(result.Upper, Is.EqualTo(source.Upper));
        Assert.That(result.Optional, Is.Null);
    }

    /// <summary>Checks collection framing, all map kinds, both construction overloads, and custom containers.</summary>
    [Test]
    public void TestCollectionWireFormat()
    {
        var source = new Containers { Binary = new byte[] { 0xAA, 0xBB } };
        source.Numbers.Add(0x0123);
        source.Numbers.Add(-2);
        source.Names.Add("\u00E9");
        source.Names.Add("b");
        source.Names.Add("\u00E9");
        source.Child.Value = 7;
        source.FixedItems[0].Value = 8;
        source.NewItems.Add(new Item { Value = 9 });
        source.FactoryItems.Add(new Item { Value = 10 });
        source.NumbersByName.Add("X", 0x0321);
        source.StringsByName.Add("k", "\u00E9");
        source.BinaryByName.Add("X", new byte[] { 0, 0xEF });
        source.NewByName.Add("k", new Item { Value = 11 });
        source.FactoryByName.Add("k", new Item { Value = 12 });

        byte[] expected = Convert.FromHexString(
            "41504B42010001008600000010000000" + // Header: string table at 134, 16 strings.
            "0C" +
            "000802AABB" +                      // Binary.
            "010A02022301FEFF" +                // PrimitiveArray: count 2, stride 2.
            "020B03030003" +                    // StringArray: IDs 3, 0, 3.
            "04090701050307000000" +            // Table: length 7, one field.
            "060D0207010503080000000100" +      // TableArray: second item is an empty table.
            "070D010701050309000000" +          // Collection with new().
            "080D01070105030A000000" +          // Collection with factory.
            "090E060100020A2103" +              // Map: length 6, count 1, kind 0, stride 2.
            "0B0E0401010C03" +                  // Map: length 4, count 1, kind 1.
            "0D0E0601020A0200EF" +              // Map: length 6, count 1, kind 2.
            "0E0E0B01030C070105030B000000" +    // Map: length 11, count 1, kind 3; new().
            "0F0E0B01030C070105030C000000" +    // Map: same framing, with factory.
            "01620170017302C3A9017401760161016C014C01690158016A016B016D016E014E");

        Assert.That(PackedBinarySerializer.Encode(source, elideDefaults: true), Is.EqualTo(expected));
        var errors = new List<string>();
        Containers result = PackedBinarySerializer.Decode<Containers>(expected, onError: errors.Add);
        Assert.That(errors, Is.Empty);
        Assert.That(result.Binary.ToArray(), Is.EqualTo(source.Binary.ToArray()));
        Assert.That(result.Numbers, Is.EqualTo(source.Numbers));
        Assert.That(result.Names, Is.EqualTo(source.Names));
        AssertItem(result.Child, 7);
        AssertItem(result.FixedItems[0], 8);
        AssertItem(result.FixedItems[1], 0);
        AssertItem(result.NewItems.Single(), 9);
        AssertItem(result.FactoryItems.Single(), 10, factoryValue: 10);
        Assert.That(result.NumbersByName["x"], Is.EqualTo(0x0321));
        Assert.That(result.StringsByName["k"], Is.EqualTo("\u00E9"));
        Assert.That(result.BinaryByName["X"].ToArray(), Is.EqualTo(new byte[] { 0, 0xEF }));
        AssertItem(result.NewByName["k"], 11);
        AssertItem(result.FactoryByName["K"], 12, factoryValue: 12);
    }

    /// <summary>Checks that only default-valued symmetric binds are elided, not direct setters or empty tables.</summary>
    /// <param name="elideDefaults">Whether to enable default elision.</param>
    [TestCase(true)]
    [TestCase(false)]
    public void TestDefaultsAndDirectSetters(bool elideDefaults)
    {
        int amount = 37;
        SignedCode code = SignedCode.Negative;
        string text = "idle";
        var required = new Empty();
        Empty? optional = new Empty();
        var fixture = new Callback((node, _) =>
        {
            node.BindValue("amount", ref amount, 37);
            node.BindEnum("code", ref code, SignedCode.Negative);
            node.BindString("text", ref text, "idle");
            node.BindSerializable("required", required);
            node.BindSerializableOptional("optional", ref optional, static _ => new Empty());
            if (node is not SerializeWriteNode writer)
                return;

            writer.SetValue("zero", 0);
            writer.SetEnum("none", SignedCode.None);
            writer.SetString("empty", "");
            writer.SetNullableValue<int>("zero?", 0);
        });

        byte[] bytes = PackedBinarySerializer.Encode(fixture, elideDefaults: elideDefaults);
        Assert.That(bytes[6], Is.EqualTo(elideDefaults ? 1 : 0));
        Assert.That(bytes[16], Is.EqualTo(elideDefaults ? 6 : 9));
        amount = -1;
        code = SignedCode.None;
        text = "stale";
        optional = null;
        PackedBinarySerializer.Decode(bytes, reader =>
        {
            Assert.That(reader.GetValue("amount", -1), Is.EqualTo(elideDefaults ? -1 : 37));
            Assert.That(reader.GetEnum("code", SignedCode.None), Is.EqualTo(elideDefaults ? SignedCode.None : SignedCode.Negative));
            Assert.That(reader.GetString("text", "missing"), Is.EqualTo(elideDefaults ? "missing" : "idle"));
            Assert.That(reader.GetValue("zero", -1), Is.Zero);
            Assert.That(reader.GetEnum("none", SignedCode.Negative), Is.EqualTo(SignedCode.None));
            Assert.That(reader.GetString("empty", "missing"), Is.Empty);
            Assert.That(reader.GetNullableValue<int>("zero?"), Is.Zero);
            Assert.That(reader.TryGetChildNode("required", out _), Is.True);
            Assert.That(reader.TryGetChildNode("optional", out _), Is.True);
            return fixture;
        });
        Assert.That(amount, Is.EqualTo(37));
        Assert.That(code, Is.EqualTo(SignedCode.Negative));
        Assert.That(text, Is.EqualTo("idle"));
        Assert.That(optional, Is.Not.Null);
    }

    /// <summary>Checks repeatable, out-of-order factory reads and child ID peeks on both lookup paths.</summary>
    /// <param name="fieldCount">Number of fields in the root table.</param>
    [TestCase(16)]
    [TestCase(17)]
    [TestCase(130)]
    public void TestFactoryCanRepeatReadsAndPeekChildren(int fieldCount)
    {
        var child = new Item { Value = 42 };
        var saveRefs = new ReferenceContext();
        byte[] bytes = PackedBinarySerializer.Encode(new Callback((node, _) =>
        {
            node.BindSerializable("child", child);
            var writer = (SerializeWriteNode)node;
            for (int i = 1; i < fieldCount; i++)
                writer.SetValue("f" + i, i);
        }), refCtx: saveRefs);
        if (fieldCount == 130)
            Assert.That(bytes.AsSpan(16, 2).ToArray(), Is.EqualTo(new byte[] { 0x82, 0x01 }));

        string lastKey = "f" + (fieldCount - 1);
        int first = 0;
        int last = 0;
        var loadedChild = new Item();
        PackedBinarySerializer.Decode(bytes, reader =>
        {
            Assert.That(reader.GetValue<int>(lastKey), Is.EqualTo(fieldCount - 1));
            Assert.That(reader.TryGetChildNode("scalar", out _), Is.False);
            Assert.That(reader.TryGetChildNode("f1", out _), Is.False);
            Assert.That(reader.TryGetChildNode("Child", out _), Is.False);
            Assert.That(reader.TryGetChildNode("child", out var peek), Is.True);
            Assert.That(peek!.GetValue<ulong>(ReferenceContext.SerializeKey), Is.EqualTo(saveRefs.GetId(child)));
            Assert.That(peek.GetValue<int>("v"), Is.EqualTo(42));
            Assert.That(reader.GetValue<int>("f1"), Is.EqualTo(1));
            Assert.That(reader.GetValue<int>(lastKey), Is.EqualTo(fieldCount - 1));
            Assert.That(reader.TryGetChildNode("child", out peek), Is.True);
            Assert.That(peek!.GetValue<int>("v"), Is.EqualTo(42));
            return new Callback((node, _) =>
            {
                node.BindValue(lastKey, ref last);
                node.BindSerializable("child", loadedChild);
                node.BindValue("f1", ref first);
            });
        }, refCtx: new ReferenceContext());
        Assert.That(last, Is.EqualTo(fieldCount - 1));
        Assert.That(first, Is.EqualTo(1));
        AssertItem(loadedChild, 42);
    }

    /// <summary>Checks raw unmanaged widths and the binary-array fallback above a byte-sized stride.</summary>
    [Test]
    public void TestUnusualUnmanagedWidths()
    {
        AssertUnmanagedRoundTrip<Bytes12>(12, [0x04, 0x00, 0x0A, 0x02, 0x0C, 0x11]);
        AssertUnmanagedRoundTrip<Bytes24>(24, [0x04, 0x00, 0x0A, 0x02, 0x18, 0x11]);
        AssertUnmanagedRoundTrip<Bytes44>(44, [0x04, 0x00, 0x0A, 0x02, 0x2C, 0x11]);
        AssertUnmanagedRoundTrip<Bytes256>(256, [0x04, 0x00, 0x0C, 0x02, 0x80, 0x02, 0x80, 0x02, 0x11]);
    }

    /// <summary>Checks that span binds copy only the overlap and leave untouched or missing elements alone.</summary>
    [Test]
    public void TestBindMemoryUsesOnlyTheSpanSlice()
    {
        int[] source = [99, 5, 11, 17, 99];
        var input = new Callback((node, _) => node.BindMemory("m", source.AsSpan(1, 3)));
        byte[] expected = Convert.FromHexString(
            "41504B42010001002100000001000000" + "01000A0304050000000B00000011000000" + "016D");
        Assert.That(PackedBinarySerializer.Encode(input, elideDefaults: true), Is.EqualTo(expected));

        int[] shortSpan = [77, -1, -1, 77];
        int[] longSpan = [88, -1, -1, -1, -1, 88];
        int[] absent = [93, 94];
        PackedBinarySerializer.Populate(expected, new Callback((node, _) =>
        {
            node.BindMemory("m", shortSpan.AsSpan(1, 2));
            node.BindMemory("m", longSpan.AsSpan(1, 4));
            node.BindMemory("absent", absent.AsSpan());
        }));
        Assert.That(shortSpan, Is.EqualTo(new[] { 77, 5, 11, 77 }));
        Assert.That(longSpan, Is.EqualTo(new[] { 88, 5, 11, 17, -1, 88 }));
        Assert.That(absent, Is.EqualTo(new[] { 93, 94 }));
    }

    /// <summary>Checks cyclic references, forward aliases, root registration, and reuse during Populate.</summary>
    [Test]
    public void TestReferenceIdentityAndPopulate()
    {
        var second = new Item { Value = 23 };
        var source = new Graph { Optional = second, Alias = second };
        source.Child.Value = 11;
        source.Child.Link = second;
        second.Link = source.Child;
        var saveRefs = new ReferenceContext();
        byte[] bytes = PackedBinarySerializer.Encode(source, refCtx: saveRefs);
        ulong rootId = saveRefs.GetId(source);

        var loadRefs = new ReferenceContext();
        Graph decoded = PackedBinarySerializer.Decode<Graph>(bytes, refCtx: loadRefs);
        Assert.That(loadRefs.TryGetReference(rootId, out object? registered), Is.True);
        Assert.That(registered, Is.SameAs(decoded));
        Assert.That(decoded.HadAliasDuringLoad, Is.False);
        Assert.That(decoded.Child.HadLinkDuringLoad, Is.False);
        Assert.That(decoded.Optional?.HadLinkDuringLoad, Is.False);
        AssertGraph(decoded, factoryValue: 23);

        var existing = new Item { Value = -1 };
        var populated = new Graph { Optional = existing };
        var populateRefs = new ReferenceContext();
        PackedBinarySerializer.Populate(bytes, populated, refCtx: populateRefs);
        Assert.That(populateRefs.TryGetReference(rootId, out _), Is.False);
        Assert.That(populated.Optional, Is.SameAs(existing));
        AssertGraph(populated, factoryValue: null);
    }

    /// <summary>Checks that only Decode invokes PostLoad when there is no reference context.</summary>
    [Test]
    public void TestPostLoadWithoutReferenceContext()
    {
        byte[] bytes = PackedBinarySerializer.Encode(new Item { Value = 15 });
        AssertItem(PackedBinarySerializer.Decode<Item>(bytes), 15);
        var target = new Item();
        PackedBinarySerializer.Populate(bytes, target);
        Assert.That(target.Value, Is.EqualTo(15));
        Assert.That(target.PostLoads, Is.Zero);
    }

    /// <summary>Distinguishes clearing explicit nulls at load time from clearing missing references at post-load.</summary>
    /// <param name="clearMissingReferences">Whether to clear missing references during Populate.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void TestPopulateMissingReferencesAndExplicitNull(bool clearMissingReferences)
    {
        byte[] bytes = PackedBinarySerializer.Encode(new Callback(static (node, _) =>
        {
            Item? value = null;
            node.BindSerializableOptional("null", ref value, Item.Create);
        }));
        var stale = new Item();
        IReferenceable? reference = stale;
        var required = new Item { Value = 19, Link = stale };
        Item? missing = stale;
        Item? explicitNull = stale;
        bool sawNullDuringLoad = false;
        bool sawMissingValueDuringLoad = false;
        var target = new Callback((node, mode) =>
        {
            node.BindReference("ref", ref reference);
            node.BindSerializable("required", required);
            node.BindSerializableOptional("missing", ref missing, Item.Create);
            node.BindSerializableOptional("null", ref explicitNull, Item.Create);
            if (mode != SerializeMode.Load)
                return;

            sawNullDuringLoad = explicitNull is null;
            sawMissingValueDuringLoad = ReferenceEquals(missing, stale);
        });

        PackedBinarySerializer.Populate(bytes, target, refCtx: new ReferenceContext(),
            clearMissingReferences: clearMissingReferences);
        Assert.That(sawNullDuringLoad, Is.True);
        Assert.That(sawMissingValueDuringLoad, Is.True);
        Assert.That(explicitNull, Is.Null);
        Assert.That(reference, clearMissingReferences ? Is.Null : Is.SameAs(stale));
        Assert.That(missing, clearMissingReferences ? Is.Null : Is.SameAs(stale));
        Assert.That(required.Link, clearMissingReferences ? Is.Null : Is.SameAs(stale));
        Assert.That(required.Value, Is.EqualTo(19));
        Assert.That(required.PostLoads, Is.EqualTo(clearMissingReferences ? 1 : 0));
    }

    /// <summary>Checks that a later successful item gets post-loaded with its own table after an item fails.</summary>
    [Test]
    public void TestCollectionFailureDoesNotShiftPostLoadTables()
    {
        var first = new Item { Value = 10 };
        var last = new Item { Value = 20, Link = first };
        Item[] items = [first, new Item { Value = -1, Link = last }, last];
        var source = new Callback((node, _) => node.BindCollectionSerializable("items", items, Item.Create));
        byte[] bytes = PackedBinarySerializer.Encode(source, refCtx: new ReferenceContext());

        var errors = new List<string>();
        var loaded = new Collection<Item>();
        var target = new Callback((node, _) => node.BindCollectionSerializable("items", loaded, reader =>
        {
            Item item = Item.Create(reader);
            if (item.FactoryValue < 0)
                throw new InvalidOperationException("Reject this item");

            return item;
        }));
        PackedBinarySerializer.Decode(bytes, _ => target, refCtx: new ReferenceContext(), onError: errors.Add);
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(loaded.Select(static item => item.Value), Is.EqualTo(new[] { 10, 20 }));
        AssertItem(loaded[0], 10, factoryValue: 10);
        AssertItem(loaded[1], 20, factoryValue: 20);
        Assert.That(loaded[1].Link, Is.SameAs(loaded[0]));
    }

    /// <summary>Checks that post-load uses each item's original table when the destination reorders items.</summary>
    [Test]
    public void TestReorderedCollectionKeepsOriginalPostLoadTables()
    {
        var high = new Item { Value = 20 };
        var low = new Item { Value = 10, Link = high };
        high.Link = low;
        Item[] source = [high, low];
        byte[] bytes = PackedBinarySerializer.Encode(
            new Callback((node, _) => node.BindCollectionSerializable("items", source)), refCtx: new ReferenceContext());

        var sorted = new SortedSet<Item>(Comparer<Item>.Create(static (left, right) => left.Value.CompareTo(right.Value)));
        PackedBinarySerializer.Decode(bytes, _ => new Callback((node, _) => node.BindCollectionSerializable("items", sorted)),
            refCtx: new ReferenceContext());
        Item[] loaded = sorted.ToArray();
        Assert.That(loaded, Has.Length.EqualTo(2));
        AssertItem(loaded[0], 10);
        AssertItem(loaded[1], 20);
        Assert.That(loaded[0].Link, Is.SameAs(loaded[1]));
        Assert.That(loaded[1].Link, Is.SameAs(loaded[0]));
    }

    /// <summary>Checks that malformed roots report one error and never invoke the factory.</summary>
    /// <param name="hex">Packed input with one malformed section.</param>
    [TestCase("41504B420100", TestName = "TestMalformedRoot_TruncatedHeader")]
    [TestCase("41504B4202000000110000000000000000", TestName = "TestMalformedRoot_UnknownVersion")]
    [TestCase("41504B42010000000F0000000000000000", TestName = "TestMalformedRoot_InvalidStringOffset")]
    [TestCase("41504B420100000013000000010000000100010178", TestName = "TestMalformedRoot_TruncatedScalar")]
    [TestCase("41504B420100000013000000010000000100FF0178", TestName = "TestMalformedRoot_UnknownTag")]
    [TestCase("41504B420100000014000000010000000101012A0178", TestName = "TestMalformedRoot_InvalidKeyId")]
    [TestCase("41504B420100000014000000010000000100012A02C0AF", TestName = "TestMalformedRoot_InvalidUtf8")]
    [TestCase("41504B420100000012000000000000008000", TestName = "TestMalformedRoot_NonminimalCount")]
    [TestCase("41504B420100000015000000000000008080808008", TestName = "TestMalformedRoot_CountExceedsInt32")]
    public void TestMalformedRoot(string hex)
    {
        var errors = new List<string>();
        int calls = 0;
        bool success = PackedBinarySerializer.TryDecode(Convert.FromHexString(hex), _ =>
        {
            calls++;
            return new Empty();
        }, out _, onError: errors.Add);
        Assert.That(success, Is.False);
        Assert.That(calls, Is.Zero);
        Assert.That(errors, Has.Count.EqualTo(1));
    }

    /// <summary>Checks that a malformed child body with a valid length is skipped without losing siblings.</summary>
    [Test]
    public void TestCorruptChildDoesNotLoseSiblings()
    {
        byte[] bytes = Convert.FromHexString(
            "41504B42010000001C00000003000000" + "03" +
            "00090101" + // "bad": one-byte body says one field but contains none.
            "01012A" +   // "x": byte 42.
            "02090100" + // "ok": valid empty table.
            "036261640178026F6B");
        var errors = new List<string>();
        int badFactoryCalls = 0;
        byte scalar = 0;
        Item? bad = null;
        Item? good = null;
        var target = new Callback((node, _) =>
        {
            node.BindSerializableOptional("bad", ref bad, _ =>
            {
                badFactoryCalls++;
                return new Item();
            });
            node.BindValue("x", ref scalar);
            node.BindSerializableOptional("ok", ref good, Item.Create);
        });

        Assert.That(PackedBinarySerializer.TryDecode(bytes, _ => target, out _, onError: errors.Add), Is.True);
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(badFactoryCalls, Is.Zero);
        Assert.That(bad, Is.Null);
        Assert.That(scalar, Is.EqualTo(42));
        Assert.That(good, Is.Not.Null);
        AssertItem(good!, 0, factoryValue: 0);
    }

    /// <summary>Checks that a throwing error callback escapes nested binding catches unchanged.</summary>
    /// <param name="failingMode">The serialization pass in which the innermost binding fails.</param>
    [TestCase(SerializeMode.Save)]
    [TestCase(SerializeMode.Load)]
    [TestCase(SerializeMode.PostLoad)]
    public void TestErrorHandlerExceptionEscapes(SerializeMode failingMode)
    {
        int value = 1;
        var leaf = new Callback((node, mode) =>
        {
            node.BindValue("v", ref value);
            if (mode == failingMode)
                throw new FormatException("Leaf binding failed");
        });
        var child = new Callback((node, _) => node.BindSerializable("leaf", leaf));
        var root = new Callback((node, _) => node.BindSerializable("child", child));
        byte[] data = failingMode == SerializeMode.Save ? [] : PackedBinarySerializer.Encode(root);
        var expected = new InvalidOperationException("Error handler failed");
        int calls = 0;
        void OnError(string _)
        {
            calls++;
            throw expected;
        }

        var thrown = Assert.Throws<InvalidOperationException>(() =>
        {
            if (failingMode == SerializeMode.Save)
            {
                PackedBinarySerializer.Encode(root, onError: OnError);
                return;
            }

            PackedBinarySerializer.TryDecode(data, _ => root, out _, onError: OnError);
        });
        Assert.That(thrown, Is.SameAs(expected));
        Assert.That(calls, Is.EqualTo(1));
    }

    /// <summary>Checks that default encoding preserves default bindings with header flag 0 and rejects legacy payloads.</summary>
    [Test]
    public void TestDefaultEncodePreservesDefaultBindings_AndRejectsLegacyPayload()
    {
        var source = new Scalars { Int = 0, Text = "" };
        byte[] encoded = PackedBinarySerializer.Encode(source);
        Assert.That(encoded.AsSpan(0, 4).SequenceEqual("APKB"u8), Is.True);
        Assert.That(encoded[6], Is.Zero);
        Assert.That(encoded, Is.EqualTo(PackedBinarySerializer.Encode(source, elideDefaults: false)));

        byte[] legacy = new byte[sizeof(int)];
        var errors = new List<string>();
        Assert.Throws<InvalidDataException>(() => PackedBinarySerializer.Decode<Scalars>(legacy, onError: errors.Add));
        Assert.That(errors, Has.Count.EqualTo(1));
    }

    /// <summary>Checks that concurrent child-table reads on a single opened document are thread-safe.</summary>
    [Test]
    public void TestConcurrentSubtableReadsAreThreadSafe()
    {
        const int childCount = 48;
        byte[] bytes = PackedBinarySerializer.Encode(new Callback((node, _) =>
        {
            for (int i = 0; i < childCount; i++)
                node.BindSerializable($"item_{i}", new Item { Value = i * 7 + 3 });
        }));

        Assert.That(PackedBinaryReadNode.TryOpen(bytes, out PackedBinaryReadNode? root), Is.True);
        Parallel.For(0, 8, worker =>
        {
            for (int pass = 0; pass < 20; pass++)
            {
                int start = (worker * 7 + pass) % childCount;
                for (int offset = 0; offset < childCount; offset++)
                {
                    int idx = (start + offset) % childCount;
                    Assert.That(root!.TryGetChildNode($"item_{idx}", out SerializeReadNode? child), Is.True);
                    Assert.That(child!.GetValue<int>("v"), Is.EqualTo(idx * 7 + 3));
                }
            }
        });
    }

    private static void AssertItem(Item item, int value, int? factoryValue = null)
    {
        Assert.That(item.Value, Is.EqualTo(value));
        Assert.That(item.FactoryValue, Is.EqualTo(factoryValue));
        Assert.That(item.PostLoads, Is.EqualTo(1));
    }

    private static void AssertGraph(Graph graph, int? factoryValue)
    {
        Assert.That(graph.Optional, Is.Not.Null);
        AssertItem(graph.Child, 11);
        AssertItem(graph.Optional!, 23, factoryValue);
        Assert.That(graph.Alias, Is.SameAs(graph.Optional));
        Assert.That(graph.Child.Link, Is.SameAs(graph.Optional));
        Assert.That(graph.Optional!.Link, Is.SameAs(graph.Child));
    }

    private static void AssertUnmanagedRoundTrip<T>(int width, byte[] arrayPrefix) where T : unmanaged
    {
        T first = default;
        T second = default;
        MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref first, 1)).Fill(0x11);
        MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref second, 1)).Fill(0xE7);

        var source = new UnmanagedFields<T> { Scalar = first, Nullable = second };
        source.Values.Add(first);
        source.Values.Add(second);
        source.Map.Add("k", second);
        byte[] bytes = PackedBinarySerializer.Encode(source, elideDefaults: false);
        Assert.That(Unsafe.SizeOf<T>(), Is.EqualTo(width));
        Assert.That(bytes.AsSpan(16, arrayPrefix.Length).ToArray(), Is.EqualTo(arrayPrefix));
        // The next field is scalar key ID 1 with tag 8, after both complete array items.
        int scalarOffset = 16 + arrayPrefix.Length - 1 + 2 * width;
        Assert.That(bytes.AsSpan(scalarOffset, 2).ToArray(), Is.EqualTo(new byte[] { 1, 8 }));

        UnmanagedFields<T> result = PackedBinarySerializer.Decode<UnmanagedFields<T>>(bytes);
        AssertBytes(result.Scalar, 0x11, width);
        Assert.That(result.Nullable.HasValue, Is.True);
        AssertBytes(result.Nullable.GetValueOrDefault(), 0xE7, width);
        Assert.That(result.Values, Has.Count.EqualTo(2));
        AssertBytes(result.Values[0], 0x11, width);
        AssertBytes(result.Values[1], 0xE7, width);
        AssertBytes(result.Map["k"], 0xE7, width);
    }

    private static void AssertBytes<T>(T value, byte expected, int width) where T : unmanaged
    {
        byte[] bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1)).ToArray();
        Assert.That(bytes, Is.EqualTo(Enumerable.Repeat(expected, width)));
    }

    private enum SignedCode : short { None, Negative = -2 }

    private sealed class Scalars : ISerializable
    {
        public byte Byte;
        public SignedCode Code;
        public int Int;
        public ulong Long;
        public UInt128 Wide;
        public int? Present;
        public int? Absent;
        public string Text = "";
        public string Shared = "";
        public string Upper = "";
        public Empty? Optional;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindValue("a", ref Byte);
            node.BindEnum("b", ref Code);
            node.BindValue("c", ref Int);
            node.BindValue("d", ref Long);
            node.BindValue("e", ref Wide);
            node.BindValue("f", ref Present);
            node.BindValue("g", ref Absent);
            node.BindString("h", ref Text);
            node.BindString("\u96EA", ref Shared);
            node.BindString("A", ref Upper);
            node.BindSerializableOptional("z", ref Optional, static _ => new Empty());
        }
    }

    private sealed class Containers : ISerializable
    {
        public ReadOnlyMemory<byte> Binary;
        public readonly Collection<short> Numbers = new();
        public readonly Collection<string> Names = new();
        public readonly Item Child = new();
        public readonly Item[] FixedItems = [new(), new()];
        public readonly Collection<Item> NewItems = new();
        public readonly Collection<Item> FactoryItems = new();
        public readonly SortedDictionary<string, short> NumbersByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly SortedDictionary<string, string> StringsByName = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, ReadOnlyMemory<byte>> BinaryByName = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, Item> NewByName = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, Item> FactoryByName = new(StringComparer.OrdinalIgnoreCase);

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindBinary("b", ref Binary);
            node.BindCollection("p", Numbers);
            node.BindCollection("s", Names);
            node.BindSerializable("t", Child);
            node.BindArraySerializable("a", FixedItems);
            node.BindCollectionSerializable("l", NewItems);
            node.BindCollectionSerializable("L", FactoryItems, Item.Create);
            node.BindDictionary("i", NumbersByName);
            node.BindDictionary("j", StringsByName);
            node.BindDictionary("m", BinaryByName);
            node.BindDictionarySerializable("n", NewByName);
            node.BindDictionarySerializable("N", FactoryByName, Item.Create);
        }
    }

    private sealed class Graph : IReferenceable
    {
        public readonly Item Child = new();
        public Item? Optional;
        public IReferenceable? Alias;
        public bool HadAliasDuringLoad;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindReference("alias", ref Alias);
            node.BindSerializable("child", Child);
            node.BindSerializableOptional("optional", ref Optional, Item.Create);
            if (mode == SerializeMode.Load)
                HadAliasDuringLoad = Alias is not null;
        }
    }

    private sealed class Item : IReferenceable
    {
        public int Value;
        public IReferenceable? Link;
        public bool HadLinkDuringLoad;
        public int PostLoads;
        public int? FactoryValue { get; private init; }

        public static Item Create(SerializeReadNode reader) => new() { FactoryValue = reader.GetValue<int>("v") };

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindValue("v", ref Value);
            node.BindReference("link", ref Link);
            if (mode == SerializeMode.Load)
                HadLinkDuringLoad = Link is not null;
            if (mode == SerializeMode.PostLoad)
                PostLoads++;
        }
    }

    private sealed class UnmanagedFields<T> : ISerializable where T : unmanaged
    {
        public T Scalar;
        public T? Nullable;
        public readonly List<T> Values = new();
        public readonly Dictionary<string, T> Map = new(StringComparer.Ordinal);

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindCollection("a", Values);
            node.BindValue("v", ref Scalar);
            node.BindValue("n", ref Nullable);
            node.BindDictionary("m", Map);
        }
    }

    [InlineArray(12)] private struct Bytes12 { private byte _element0; }
    [InlineArray(24)] private struct Bytes24 { private byte _element0; }
    [InlineArray(44)] private struct Bytes44 { private byte _element0; }
    [InlineArray(256)] private struct Bytes256 { private byte _element0; }

    private sealed class Empty : ISerializable
    {
        public void OnSerialize(SerializeNode node, SerializeMode mode) { }
    }

    private sealed class Callback(Action<SerializeNode, SerializeMode> bind) : ISerializable
    {
        public void OnSerialize(SerializeNode node, SerializeMode mode) => bind(node, mode);
    }
}
