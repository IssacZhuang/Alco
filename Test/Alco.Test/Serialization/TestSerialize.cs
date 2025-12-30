using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

#nullable enable

namespace Alco.Test;

public enum TestEnum
{
    None,
    One,
    Two,
    Three
}

public class TestSerialize
{
    // Tests for BindArraySerializable
    private class ArrayItem : IReferenceable
    {
        public int Value;
        public bool PostLoaded;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindValue("value", ref Value);
            if (mode == SerializeMode.PostLoad)
            {
                PostLoaded = true;
            }
        }
    }

    private class ArrayContainer : ISerializable
    {
        public List<ArrayItem> Items;
        public ArrayItem? Ref1;
        public ArrayItem? Ref2;

        public ArrayContainer()
        {
            Items = new List<ArrayItem> { new ArrayItem(), new ArrayItem(), new ArrayItem() };
        }

        public ArrayContainer(int length)
        {
            Items = new List<ArrayItem>(length);
            for (int i = 0; i < length; i++)
            {
                Items.Add(new ArrayItem());
            }
        }

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindArraySerializable("items", Items);
            node.BindReference("ref1", ref Ref1);
            node.BindReference("ref2", ref Ref2);
        }
    }

    [Test]
    public void TestBindArraySerializable_BasicRoundTripAndPostLoad()
    {
        ArrayContainer src = new ArrayContainer();
        src.Items[0].Value = 10;
        src.Items[1].Value = 20;
        src.Items[2].Value = 30;
        src.Ref1 = src.Items[0];
        src.Ref2 = src.Items[2];

        ReadOnlyMemory<byte> bytes = BinaryParser.Encode(src, null, new ReferenceContext());
        ArrayContainer dst = BinaryParser.Decode<ArrayContainer>(bytes.Span, null, new ReferenceContext());

        Assert.That(dst.Items.Count, Is.EqualTo(3));
        Assert.That(dst.Items[0].Value, Is.EqualTo(10));
        Assert.That(dst.Items[1].Value, Is.EqualTo(20));
        Assert.That(dst.Items[2].Value, Is.EqualTo(30));
        Assert.That(dst.Items[0].PostLoaded, Is.True);
        Assert.That(dst.Items[1].PostLoaded, Is.True);
        Assert.That(dst.Items[2].PostLoaded, Is.True);
        Assert.That(ReferenceEquals(dst.Items[0], dst.Ref1), Is.True);
        Assert.That(ReferenceEquals(dst.Items[2], dst.Ref2), Is.True);
    }

    [Test]
    public void TestBindArraySerializable_SizeMismatch_Saved3_Dest2()
    {
        ArrayContainer src = new ArrayContainer(3);
        src.Items[0].Value = 1;
        src.Items[1].Value = 2;
        src.Items[2].Value = 3;
        src.Ref1 = src.Items[0];
        src.Ref2 = src.Items[2];

        ReadOnlyMemory<byte> bytes = BinaryParser.Encode(src, null, new ReferenceContext());
        ArrayContainer dst = BinaryParser.Decode<ArrayContainer>(bytes.Span, static (SerializeReadNode _) => new ArrayContainer(2), null, new ReferenceContext());

        Assert.That(dst.Items.Count, Is.EqualTo(2));
        Assert.That(dst.Items[0].Value, Is.EqualTo(1));
        Assert.That(dst.Items[1].Value, Is.EqualTo(2));
        Assert.That(dst.Items[0].PostLoaded, Is.True);
        Assert.That(dst.Items[1].PostLoaded, Is.True);
        Assert.That(ReferenceEquals(dst.Items[0], dst.Ref1), Is.True);
        // ref2 points to a non-deserialized item (index 2), should be unresolved
        Assert.That(dst.Ref2, Is.Null);
    }

    [Test]
    public void TestBindArraySerializable_SizeMismatch_Saved2_Dest3()
    {
        ArrayContainer src = new ArrayContainer(2);
        src.Items[0].Value = 100;
        src.Items[1].Value = 200;
        src.Ref1 = src.Items[1];
        src.Ref2 = null;

        ReadOnlyMemory<byte> bytes = BinaryParser.Encode(src, null, new ReferenceContext());
        ArrayContainer dst = BinaryParser.Decode<ArrayContainer>(bytes.Span, static (SerializeReadNode _) => new ArrayContainer(3), null, new ReferenceContext());

        Assert.That(dst.Items.Count, Is.EqualTo(3));
        Assert.That(dst.Items[0].Value, Is.EqualTo(100));
        Assert.That(dst.Items[1].Value, Is.EqualTo(200));
        Assert.That(dst.Items[2].Value, Is.EqualTo(0));
        Assert.That(dst.Items[0].PostLoaded, Is.True);
        Assert.That(dst.Items[1].PostLoaded, Is.True);
        // Third element wasn't present in data, so it shouldn't be post-loaded
        Assert.That(dst.Items[2].PostLoaded, Is.False);
        Assert.That(ReferenceEquals(dst.Items[1], dst.Ref1), Is.True);
        Assert.That(dst.Ref2, Is.Null);
    }
    private class TestBitmap : ISerializable
    {
        private int _width;
        private int _height;
        private byte[] _data;

        public int Width => _width;
        public int Height => _height;

        public byte[] Data => _data;

        public TestBitmap(int width, int height)
        {
            _width = width;
            _height = height;
            _data = new byte[width * height];
        }

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindValue("width", ref _width);
            node.BindValue("height", ref _height);
            node.BindMemory("data", _data.AsSpan());
        }
    }

    private class TestObject1 : ISerializable
    {
        public int intValue;

        public string str;
        public List<int> listInt;
        public List<string> listStr;

        public int[] intArray;

        public TestBitmap? bitmap;

        public TestObject1()
        {
            intValue = 0;
            str = "";
            listInt = new List<int>();
            listStr = new List<string>();
            intArray = new int[10];
        }
        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindValue("intValue", ref intValue);
            node.BindString("str", ref str);
            node.BindCollection("listInt", listInt);
            node.BindCollection("listStr", listStr);
            node.BindMemory("intArray", intArray.AsSpan());
            node.BindSerializableOptional("bitmap", ref bitmap, static (SerializeReadNode subNode) =>
            {
                int width = subNode.GetValue<int>("width");
                int height = subNode.GetValue<int>("height");
                return new TestBitmap(width, height);
            });
        }
    }

    private struct TestStruct : ISerializable
    {
        public int intValue;
        public string str;


        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindValue("intValue", ref intValue);
            node.BindString("str", ref str);
        }
    }

    private class TestObjectDeep : ISerializable
    {
        public TestObject1 child;
        public TestStruct structChild;
        public TestObjectDeep()
        {
            child = new TestObject1();
            structChild = new TestStruct
            {
                intValue = 123,
                str = "123"
            };
        }

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindSerializable("obj", child);
            node.BindSerializable("struct", structChild);

        }
    }

    private class TestObjectCollectionDeep : ISerializable
    {
        public List<TestObject1> list;
        public List<TestStruct> listStruct;
        public TestObjectCollectionDeep()
        {
            list = new List<TestObject1>();
            listStruct = new List<TestStruct>();
        }
        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindCollectionSerializable("list", list);
            node.BindCollectionSerializable("listStruct", listStruct);
        }
    }

    private class TestObjectEnum : ISerializable
    {
        public TestEnum enumValue;
        public List<TestEnum> enumList;

        public TestObjectEnum()
        {
            enumValue = TestEnum.None;
            enumList = new List<TestEnum>();
        }

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindEnum("enumValue", ref enumValue);
            node.BindCollection("enumList", enumList);
        }
    }

    private class TestObjectDictionary : ISerializable
    {
        public Dictionary<string, int> dictInt;
        public Dictionary<string, string> dictString;
        public Dictionary<string, ReadOnlyMemory<byte>> dictByteArray;

        public TestObjectDictionary()
        {
            dictInt = new Dictionary<string, int>();
            dictString = new Dictionary<string, string>();
            dictByteArray = new Dictionary<string, ReadOnlyMemory<byte>>();
        }

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindDictionary("dictInt", dictInt);
            node.BindDictionary("dictString", dictString);
            node.BindDictionary("dictByteArray", dictByteArray);
        }
    }

    private class TestObjectDictionarySerializable : ISerializable
    {
        public Dictionary<string, TestObject1> dictObj;
        public Dictionary<string, TestStruct> dictStruct;

        public TestObjectDictionarySerializable()
        {
            dictObj = new Dictionary<string, TestObject1>();
            dictStruct = new Dictionary<string, TestStruct>();
        }

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindDictionarySerializable("dictObj", dictObj);
            node.BindDictionarySerializable("dictStruct", dictStruct);
        }
    }

    private class TestObjectDictionaryFactory : ISerializable
    {
        public Dictionary<string, TestBitmap> dictBitmap;

        public TestObjectDictionaryFactory()
        {
            dictBitmap = new Dictionary<string, TestBitmap>();
        }

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindDictionarySerializable("dictBitmap", dictBitmap, static (SerializeReadNode n) =>
            {
                int width = n.GetValue<int>("width");
                int height = n.GetValue<int>("height");
                return new TestBitmap(width, height);
            });
        }
    }

    private class TestObjectBinary : ISerializable
    {
        public ReadOnlyMemory<byte> binaryData;
        public ReadOnlyMemory<byte> emptyBinaryData;

        public TestObjectBinary()
        {
            binaryData = ReadOnlyMemory<byte>.Empty;
            emptyBinaryData = ReadOnlyMemory<byte>.Empty;
        }

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindBinary("binaryData", ref binaryData);
            node.BindBinary("emptyBinaryData", ref emptyBinaryData);
        }
    }

    [Test]
    public void TestSerializeNormal()
    {
        TestObject1 obj = new TestObject1();
        obj.intValue = 10;
        obj.str = "Hello";
        obj.intArray[0] = 1;
        obj.intArray[5] = 2;
        obj.intArray[9] = 3;

        obj.bitmap = null;

        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());
        TestObject1 obj2 = BinaryParser.Decode<TestObject1>(data, null, new ReferenceContext());
        Assert.That(obj2.intValue, Is.EqualTo(10));
        Assert.That(obj2.str, Is.EqualTo("Hello"));
        Assert.That(obj2.intArray[0], Is.EqualTo(1));
        Assert.That(obj2.intArray[5], Is.EqualTo(2));
        Assert.That(obj2.intArray[9], Is.EqualTo(3));
        Assert.That(obj2.bitmap, Is.Null);

        obj.bitmap = new TestBitmap(10, 10);
        data = BinaryParser.Encode(obj, null, new ReferenceContext());
        TestObject1 obj3 = BinaryParser.Decode<TestObject1>(data, null, new ReferenceContext());
        Assert.That(obj3.bitmap, Is.Not.Null);
        Assert.That(obj3.bitmap.Width, Is.EqualTo(10));
        Assert.That(obj3.bitmap.Height, Is.EqualTo(10));
        Assert.That(obj3.bitmap.Data.Length, Is.EqualTo(10 * 10));

    }

    [Test]
    public void TestSerializeCollection()
    {
        int[] ints = [1, 2, 3, 4, 5];
        string[] strings = ["a", "b", "c", "d", "e"];
        TestObject1 obj = new TestObject1();
        obj.listInt.AddRange(ints);
        obj.listStr.AddRange(strings);
        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());
        TestObject1 obj2 = BinaryParser.Decode<TestObject1>(data, null, new ReferenceContext());
        for (int i = 0; i < ints.Length; i++)
        {
            Assert.That(obj2.listInt[i], Is.EqualTo(ints[i]));
        }
        for (int i = 0; i < strings.Length; i++)
        {
            Assert.That(obj2.listStr[i], Is.EqualTo(strings[i]));
        }
    }

    [Test]
    public void TestSerializeDeep()
    {
        TestObjectDeep obj = new TestObjectDeep();
        obj.child.intValue = 10;
        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());
        TestObjectDeep obj2 = BinaryParser.Decode<TestObjectDeep>(data, null, new ReferenceContext());
        Assert.IsTrue(obj2.child.intValue == 10);
        Assert.That(obj2.child.str, Is.EqualTo(""));
        Assert.That(obj2.structChild.intValue, Is.EqualTo(123));
        Assert.That(obj2.structChild.str, Is.EqualTo("123"));
    }

    [Test]
    public void TestSerializeCollectionDeep()
    {
        int[] ints = [1, 2, 3, 4, 5];
        TestObjectCollectionDeep obj = new TestObjectCollectionDeep();
        for (int i = 0; i < ints.Length; i++)
        {
            TestObject1 o = new TestObject1();
            o.intValue = ints[i];
            obj.list.Add(o);
            TestStruct s = new TestStruct
            {
                intValue = ints[i],
                str = ints[i].ToString()
            };
            obj.listStruct.Add(s);
        }

        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());
        TestObjectCollectionDeep obj2 = BinaryParser.Decode<TestObjectCollectionDeep>(data, null, new ReferenceContext());
        for (int i = 0; i < ints.Length; i++)
        {
            Assert.That(obj2.list[i].intValue, Is.EqualTo(ints[i]));
            Assert.That(obj2.list[i].str, Is.EqualTo(""));
            Assert.That(obj2.listStruct[i].intValue, Is.EqualTo(ints[i]));
            Assert.That(obj2.listStruct[i].str, Is.EqualTo(ints[i].ToString()));
        }
    }

    [Test]
    public void TestSerializeEnum()
    {
        TestObjectEnum obj = new TestObjectEnum();
        obj.enumValue = TestEnum.Two;
        obj.enumList.Add(TestEnum.One);
        obj.enumList.Add(TestEnum.Two);
        obj.enumList.Add(TestEnum.Three);

        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());
        TestObjectEnum obj2 = BinaryParser.Decode<TestObjectEnum>(data, null, new ReferenceContext());

        Assert.That(obj2.enumValue, Is.EqualTo(TestEnum.Two));
        Assert.That(obj2.enumList.Count, Is.EqualTo(3));
        Assert.That(obj2.enumList[0], Is.EqualTo(TestEnum.One));
        Assert.That(obj2.enumList[1], Is.EqualTo(TestEnum.Two));
        Assert.That(obj2.enumList[2], Is.EqualTo(TestEnum.Three));
    }

    [Test]
    public void TestSerializeDictionary()
    {
        TestObjectDictionary obj = new TestObjectDictionary();

        // Setup test data for integer dictionary
        obj.dictInt["key1"] = 100;
        obj.dictInt["key2"] = 200;
        obj.dictInt["key3"] = 300;

        // Setup test data for string dictionary
        obj.dictString["name"] = "John";
        obj.dictString["city"] = "New York";
        obj.dictString["country"] = "USA";

        // Setup test data for byte array dictionary
        obj.dictByteArray["data1"] = new byte[] { 1, 2, 3, 4, 5 }.AsMemory();
        obj.dictByteArray["data2"] = new byte[] { 10, 20, 30 }.AsMemory();
        obj.dictByteArray["data3"] = new byte[] { 255, 0, 128 }.AsMemory();

        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());
        TestObjectDictionary obj2 = BinaryParser.Decode<TestObjectDictionary>(data, null, new ReferenceContext());

        // Verify integer dictionary
        Assert.That(obj2.dictInt.Count, Is.EqualTo(3));
        Assert.That(obj2.dictInt["key1"], Is.EqualTo(100));
        Assert.That(obj2.dictInt["key2"], Is.EqualTo(200));
        Assert.That(obj2.dictInt["key3"], Is.EqualTo(300));

        // Verify string dictionary
        Assert.That(obj2.dictString.Count, Is.EqualTo(3));
        Assert.That(obj2.dictString["name"], Is.EqualTo("John"));
        Assert.That(obj2.dictString["city"], Is.EqualTo("New York"));
        Assert.That(obj2.dictString["country"], Is.EqualTo("USA"));

        // Verify byte array dictionary
        Assert.That(obj2.dictByteArray.Count, Is.EqualTo(3));
        Assert.That(obj2.dictByteArray["data1"].Span.SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }), Is.True);
        Assert.That(obj2.dictByteArray["data2"].Span.SequenceEqual(new byte[] { 10, 20, 30 }), Is.True);
        Assert.That(obj2.dictByteArray["data3"].Span.SequenceEqual(new byte[] { 255, 0, 128 }), Is.True);
    }

    [Test]
    public void TestSerializeDictionaryEmpty()
    {
        TestObjectDictionary obj = new TestObjectDictionary();
        // Leave dictionaries empty

        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());
        TestObjectDictionary obj2 = BinaryParser.Decode<TestObjectDictionary>(data, null, new ReferenceContext());

        // Verify all dictionaries are empty
        Assert.That(obj2.dictInt.Count, Is.EqualTo(0));
        Assert.That(obj2.dictString.Count, Is.EqualTo(0));
        Assert.That(obj2.dictByteArray.Count, Is.EqualTo(0));
    }

    [Test]
    public void TestSerializeDictionaryWithSpecialCharacters()
    {
        TestObjectDictionary obj = new TestObjectDictionary();

        // Test with special characters in keys and values
        obj.dictString["key with spaces"] = "value with spaces";
        obj.dictString["key\nwith\nnewlines"] = "value\nwith\nnewlines";
        obj.dictString["key\twith\ttabs"] = "value\twith\ttabs";
        obj.dictString[""] = "empty key";
        obj.dictString["null_test"] = "";

        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());
        TestObjectDictionary obj2 = BinaryParser.Decode<TestObjectDictionary>(data, null, new ReferenceContext());

        Assert.That(obj2.dictString.Count, Is.EqualTo(5));
        Assert.That(obj2.dictString["key with spaces"], Is.EqualTo("value with spaces"));
        Assert.That(obj2.dictString["key\nwith\nnewlines"], Is.EqualTo("value\nwith\nnewlines"));
        Assert.That(obj2.dictString["key\twith\ttabs"], Is.EqualTo("value\twith\ttabs"));
        Assert.That(obj2.dictString[""], Is.EqualTo("empty key"));
        Assert.That(obj2.dictString["null_test"], Is.EqualTo(""));
    }

    [Test]
    public void TestSerializeDictionarySerializable()
    {
        TestObjectDictionarySerializable obj = new TestObjectDictionarySerializable();

        TestObject1 item1 = new TestObject1();
        item1.intValue = 111;
        item1.str = "item1";
        obj.dictObj["key1"] = item1;

        TestObject1 item2 = new TestObject1();
        item2.intValue = 222;
        item2.str = "item2";
        obj.dictObj["key2"] = item2;

        TestStruct struct1 = new TestStruct { intValue = 333, str = "struct1" };
        obj.dictStruct["s1"] = struct1;

        TestStruct struct2 = new TestStruct { intValue = 444, str = "struct2" };
        obj.dictStruct["s2"] = struct2;

        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());
        TestObjectDictionarySerializable obj2 = BinaryParser.Decode<TestObjectDictionarySerializable>(data, null, new ReferenceContext());

        Assert.That(obj2.dictObj.Count, Is.EqualTo(2));
        Assert.That(obj2.dictObj["key1"].intValue, Is.EqualTo(111));
        Assert.That(obj2.dictObj["key1"].str, Is.EqualTo("item1"));
        Assert.That(obj2.dictObj["key2"].intValue, Is.EqualTo(222));
        Assert.That(obj2.dictObj["key2"].str, Is.EqualTo("item2"));

        Assert.That(obj2.dictStruct.Count, Is.EqualTo(2));
        Assert.That(obj2.dictStruct["s1"].intValue, Is.EqualTo(333));
        Assert.That(obj2.dictStruct["s1"].str, Is.EqualTo("struct1"));
        Assert.That(obj2.dictStruct["s2"].intValue, Is.EqualTo(444));
        Assert.That(obj2.dictStruct["s2"].str, Is.EqualTo("struct2"));
    }

    [Test]
    public void TestSerializeDictionaryFactory()
    {
        TestObjectDictionaryFactory obj = new TestObjectDictionaryFactory();
        
        obj.dictBitmap["icon1"] = new TestBitmap(16, 16);
        obj.dictBitmap["icon2"] = new TestBitmap(32, 32);

        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());
        TestObjectDictionaryFactory obj2 = BinaryParser.Decode<TestObjectDictionaryFactory>(data, null, new ReferenceContext());

        Assert.That(obj2.dictBitmap.Count, Is.EqualTo(2));
        Assert.That(obj2.dictBitmap["icon1"].Width, Is.EqualTo(16));
        Assert.That(obj2.dictBitmap["icon1"].Height, Is.EqualTo(16));
        Assert.That(obj2.dictBitmap["icon2"].Width, Is.EqualTo(32));
        Assert.That(obj2.dictBitmap["icon2"].Height, Is.EqualTo(32));
    }

    [Test]
    public void TestBindBinary()
    {
        TestObjectBinary obj = new TestObjectBinary();

        // Setup test data
        byte[] testData = new byte[] { 1, 2, 3, 4, 5, 255, 0, 128 };
        obj.binaryData = testData.AsMemory();
        obj.emptyBinaryData = ReadOnlyMemory<byte>.Empty;

        // Serialize
        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());

        // Deserialize
        TestObjectBinary obj2 = BinaryParser.Decode<TestObjectBinary>(data, null, new ReferenceContext());

        // Verify binary data
        Assert.That(obj2.binaryData.Span.SequenceEqual(testData), Is.True);
        Assert.That(obj2.binaryData.Length, Is.EqualTo(8));
        Assert.That(obj2.binaryData.Span[0], Is.EqualTo(1));
        Assert.That(obj2.binaryData.Span[7], Is.EqualTo(128));

        // Verify empty binary data
        Assert.That(obj2.emptyBinaryData.IsEmpty, Is.True);
        Assert.That(obj2.emptyBinaryData.Length, Is.EqualTo(0));
    }

    [Test]
    public void TestBindBinary_EmptyArray()
    {
        TestObjectBinary obj = new TestObjectBinary();
        obj.binaryData = ReadOnlyMemory<byte>.Empty;
        obj.emptyBinaryData = ReadOnlyMemory<byte>.Empty;

        // Serialize
        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());

        // Deserialize
        TestObjectBinary obj2 = BinaryParser.Decode<TestObjectBinary>(data, null, new ReferenceContext());

        // Verify both arrays are empty
        Assert.That(obj2.binaryData.IsEmpty, Is.True);
        Assert.That(obj2.binaryData.Length, Is.EqualTo(0));
        Assert.That(obj2.emptyBinaryData.IsEmpty, Is.True);
        Assert.That(obj2.emptyBinaryData.Length, Is.EqualTo(0));
    }

    [Test]
    public void TestBindBinary_LargeData()
    {
        TestObjectBinary obj = new TestObjectBinary();

        // Create a larger binary data array (10KB)
        byte[] largeData = new byte[10240];
        for (int i = 0; i < largeData.Length; i++)
        {
            largeData[i] = (byte)(i % 256);
        }
        obj.binaryData = largeData.AsMemory();
        obj.emptyBinaryData = ReadOnlyMemory<byte>.Empty;

        // Serialize
        ReadOnlyMemory<byte> data = BinaryParser.Encode(obj, null, new ReferenceContext());

        // Deserialize
        TestObjectBinary obj2 = BinaryParser.Decode<TestObjectBinary>(data, null, new ReferenceContext());

        // Verify large binary data
        Assert.That(obj2.binaryData.Span.SequenceEqual(largeData), Is.True);
        Assert.That(obj2.binaryData.Length, Is.EqualTo(10240));
        for (int i = 0; i < largeData.Length; i++)
        {
            Assert.That(obj2.binaryData.Span[i], Is.EqualTo((byte)(i % 256)));
        }

        // Verify empty array is still empty
        Assert.That(obj2.emptyBinaryData.IsEmpty, Is.True);
    }
}