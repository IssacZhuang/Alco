using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

#nullable enable

namespace Vocore.Test;

public class TestSerialize
{
    private class TestObject1 : ISerializable
    {
        public int intValue;

        public string str;
        public List<int> listInt;
        public List<string> listStr;
        public TestObject1()
        {
            intValue = 0;
            str = "";
            listInt = new List<int>();
            listStr = new List<string>();
        }
        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindValue("intValue", ref intValue);
            node.BindString("str", ref str);
            node.BindCollection("listInt", listInt);
            node.BindCollection("listStr", listStr);
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
            node.BindDeep("obj", ref child);
            node.BindDeep("struct", ref structChild);

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

    [Test]
    public void TestSerializeNormal()
    {
        TestObject1 obj = new TestObject1();
        obj.intValue = 10;
        obj.str = "Hello";
        byte[] data = BinaryParser.Encode(obj);
        TestObject1 obj2 = BinaryParser.Decode<TestObject1>(data);
        Assert.That(obj2.intValue, Is.EqualTo(10));
        Assert.That(obj2.str, Is.EqualTo("Hello"));

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
}