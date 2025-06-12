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
            node.BindMemory("data", _data);
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
            node.BindMemory("intArray", intArray);
            node.BindDeepNullable("bitmap", ref bitmap, static (SerializeReadNode subNode) =>
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
            node.BindDeep("obj", child);
            node.BindDeep("struct", structChild);

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
            node.BindCollectionDeep("list", list);
            node.BindCollectionDeep("listStruct", listStruct);
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
        public Dictionary<string, byte[]> dictByteArray;

        public TestObjectDictionary()
        {
            dictInt = new Dictionary<string, int>();
            dictString = new Dictionary<string, string>();
            dictByteArray = new Dictionary<string, byte[]>();
        }

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindDictionary("dictInt", dictInt);
            node.BindDictionary("dictString", dictString);
            node.BindDictionary("dictByteArray", dictByteArray);
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

        byte[] data = BinaryParser.Encode(obj);
        TestObject1 obj2 = BinaryParser.Decode<TestObject1>(data);
        Assert.That(obj2.intValue, Is.EqualTo(10));
        Assert.That(obj2.str, Is.EqualTo("Hello"));
        Assert.That(obj2.intArray[0], Is.EqualTo(1));
        Assert.That(obj2.intArray[5], Is.EqualTo(2));
        Assert.That(obj2.intArray[9], Is.EqualTo(3));
        Assert.That(obj2.bitmap, Is.Null);

        obj.bitmap = new TestBitmap(10, 10);
        data = BinaryParser.Encode(obj);
        TestObject1 obj3 = BinaryParser.Decode<TestObject1>(data);
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
        byte[] data = BinaryParser.Encode(obj);
        TestObject1 obj2 = BinaryParser.Decode<TestObject1>(data);
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
        byte[] data = BinaryParser.Encode(obj);
        TestObjectDeep obj2 = BinaryParser.Decode<TestObjectDeep>(data);
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

        byte[] data = BinaryParser.Encode(obj);
        TestObjectCollectionDeep obj2 = BinaryParser.Decode<TestObjectCollectionDeep>(data);
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

        byte[] data = BinaryParser.Encode(obj);
        TestObjectEnum obj2 = BinaryParser.Decode<TestObjectEnum>(data);

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
        obj.dictByteArray["data1"] = new byte[] { 1, 2, 3, 4, 5 };
        obj.dictByteArray["data2"] = new byte[] { 10, 20, 30 };
        obj.dictByteArray["data3"] = new byte[] { 255, 0, 128 };

        byte[] data = BinaryParser.Encode(obj);
        TestObjectDictionary obj2 = BinaryParser.Decode<TestObjectDictionary>(data);

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
        Assert.That(obj2.dictByteArray["data1"], Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.That(obj2.dictByteArray["data2"], Is.EqualTo(new byte[] { 10, 20, 30 }));
        Assert.That(obj2.dictByteArray["data3"], Is.EqualTo(new byte[] { 255, 0, 128 }));
    }

    [Test]
    public void TestSerializeDictionaryEmpty()
    {
        TestObjectDictionary obj = new TestObjectDictionary();
        // Leave dictionaries empty

        byte[] data = BinaryParser.Encode(obj);
        TestObjectDictionary obj2 = BinaryParser.Decode<TestObjectDictionary>(data);

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

        byte[] data = BinaryParser.Encode(obj);
        TestObjectDictionary obj2 = BinaryParser.Decode<TestObjectDictionary>(data);

        Assert.That(obj2.dictString.Count, Is.EqualTo(5));
        Assert.That(obj2.dictString["key with spaces"], Is.EqualTo("value with spaces"));
        Assert.That(obj2.dictString["key\nwith\nnewlines"], Is.EqualTo("value\nwith\nnewlines"));
        Assert.That(obj2.dictString["key\twith\ttabs"], Is.EqualTo("value\twith\ttabs"));
        Assert.That(obj2.dictString[""], Is.EqualTo("empty key"));
        Assert.That(obj2.dictString["null_test"], Is.EqualTo(""));
    }
}