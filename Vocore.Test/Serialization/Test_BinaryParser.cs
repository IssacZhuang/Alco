using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Vocore.Test
{
    public class Test_BinaryParser
    {
        public static BinaryArray NoiseArray()
        {
            string[] data = new string[5]{
                "value1",
                "value2",
                null,
                "value4",
                "value5",
            };

            BinaryArray binArray = new BinaryArray();

            foreach (var item in data)
            {
                if (item == null)
                {
                    binArray.Add(new BinaryValue());
                    continue;
                }
                byte[] value = Encoding.UTF8.GetBytes(item);
                binArray.Add(value);
            }
            return binArray;
        }

        [Test("Test BinaryParser Fast string bytes convert")]
        public void Test_FastStringBytesConvert()
        {
            string str = "abslk\n\n\tdjfas-,./;'][1231]";
            byte[] bytes = UtilsBinary.FastStringToBytes(str);
            string str2 = UtilsBinary.FastBytesToString(bytes);
            TestHelper.AssertFalse(str != str2);
            TestHelper.PrintBlue(str2);

            int count = 1000000;

            TestHelper.Benchmark("utf8",()=>{
                for (int i = 0; i < count; i++)
                {
                    byte[] bytes2 = Encoding.UTF8.GetBytes(str);
                    string str3 = Encoding.UTF8.GetString(bytes2);
                }
            });
            
            TestHelper.Benchmark("fast",()=>{
                for (int i = 0; i < count; i++)
                {
                    byte[] bytes2 = UtilsBinary.FastStringToBytes(str);
                    string str3 = UtilsBinary.FastBytesToString(bytes2);
                }
            });
        }

        [Test("Test BinaryParser object")]
        public void Test_ParseObject()
        {

            // some random data
            KeyValuePair<string, string>[] data = new KeyValuePair<string, string>[6]{
                new KeyValuePair<string, string>("ke\ty1", "value1"),
                new KeyValuePair<string, string>("key2", "valu````~~e2"),
                new KeyValuePair<string, string>("key3", "val     ue3"),
                new KeyValuePair<string, string>("key4", null),
                new KeyValuePair<string, string>("ke\ny5", "valu......e5"),
                new KeyValuePair<string, string>("key6", "val^^&&ue6"),
            };

            KeyValuePair<string, string>[] subData = new KeyValuePair<string, string>[3]{
                new KeyValuePair<string, string>("key1", "asdasdasd"),
                new KeyValuePair<string, string>("key2", ""),
                new KeyValuePair<string, string>("key3", "s90-09-9=090"),
            };

            
            BinaryTable table = new BinaryTable();

            table["noise"] = NoiseArray();

            foreach (var item in data)
            {
                if (item.Value == null)
                {
                    table[item.Key] = new BinaryValue();
                    continue;
                }
                byte[] value = Encoding.UTF8.GetBytes(item.Value);
                table[item.Key] = value;
            }

            string keySubData = "subData";
            BinaryTable subTable = new BinaryTable();
            foreach (var item in subData)
            {
                if (item.Value == null)
                {
                    subTable[item.Key] = new BinaryValue();
                    continue;
                }
                byte[] value = Encoding.UTF8.GetBytes(item.Value);
                subTable[item.Key] = value;
            }
            table[keySubData] = subTable;

            byte[] raw = BinaryParser.Encode(table);

            BinaryTable table2 = BinaryParser.Decode(raw);

            foreach (var item in data)
            {
                if (item.Value == null)
                {
                    TestHelper.AssertFalse(table2[item.Key].Type != BinaryValueType.Null);
                    continue;
                }
                byte[] value = table2[item.Key].Bytes;
                string str = Encoding.UTF8.GetString(value);
                TestHelper.AssertFalse(str != item.Value);
            }

            BinaryTable subTable2 = table2[keySubData] as BinaryTable;
            foreach (var item in subData)
            {
                if (item.Value == null)
                {
                    TestHelper.AssertFalse(subTable2[item.Key].Type != BinaryValueType.Null);
                    continue;
                }
                byte[] value = subTable2[item.Key].Bytes;
                string str = Encoding.UTF8.GetString(value);
                TestHelper.AssertFalse(str != item.Value);
            }
        }

        [Test("Test BinaryParser list")]
        public void Test_ParseList()
        {
            // some random data
            string[] data = new string[5]{
                "value1",
                "value2",
                null,
                "value4",
                "value5",
            };

            BinaryArray binArray = new BinaryArray();

            foreach (var item in data)
            {
                if (item == null)
                {
                    binArray.Add(new BinaryValue());
                    continue;
                }
                byte[] value = Encoding.UTF8.GetBytes(item);
                binArray.Add(value);
            }

            BinaryTable binObject = new BinaryTable();
            binObject["noise1"] = null;
            binObject["list"] = binArray;
            binObject["noise2"] = "noise";
            byte[] raw = BinaryParser.Encode(binObject);

            BinaryTable binObject2 = BinaryParser.Decode(raw);
            BinaryArray binArray2 = binObject2["list"] as BinaryArray;

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == null)
                {
                    TestHelper.AssertFalse(binArray2[i].Type != BinaryValueType.Null);
                    continue;
                }
                byte[] value = binArray2[i].Bytes;
                string str = Encoding.UTF8.GetString(value);
                TestHelper.AssertFalse(str != data[i]);
            }
        }

        [Test("Test BinaryParser convert")]
        public void Test_Convert()
        {
            BinaryTable table = new BinaryTable
            {
                ["key1"] = "value1",
                ["key2"] = "value2",
                ["key3"] = null,
                ["key4"] = "value4"
            };

            byte[] raw = BinaryParser.Encode(table);

            BinaryTable table2 = BinaryParser.Decode(raw);

            TestHelper.AssertTrue(table2.TryGetString("key1", out string value1));
            TestHelper.AssertTrue(value1 == "value1");
            TestHelper.AssertTrue(table2.TryGetString("key2", out string value2));
            TestHelper.AssertTrue(value2 == "value2");
            TestHelper.AssertFalse(table2.TryGetString("key3", out string value3));
            TestHelper.AssertTrue(value3 == null);
            TestHelper.AssertTrue(table2.TryGetString("key4", out string value4));
            TestHelper.AssertTrue(value4 == "value4");
        }

        struct StructForSerialize
        {
            public int intVal;
            public string strVal;
            public float floatVal;
            public bool boolVal;
        }

        [Test("Test BinaryParser vs XML serialization")]
        public void Test_Serialization()
        {
            StructForSerialize value = new StructForSerialize
            {
                intVal = 123,
                strVal = "asdasdasd",
                floatVal = 123.456f,
                boolVal = true,
            };

            int count = 100000;

            //xml
            TestHelper.Benchmark("xml", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    XmlDocument doc = new XmlDocument();
                    XmlElement root = doc.CreateElement("root");
                    doc.AppendChild(root);
                    XmlElement element = doc.CreateElement("intVal");
                    element.InnerText = value.intVal.ToString();
                    root.AppendChild(element);
                    element = doc.CreateElement("strVal");
                    element.InnerText = value.strVal;
                    root.AppendChild(element);
                    element = doc.CreateElement("floatVal");
                    element.InnerText = value.floatVal.ToString();
                    root.AppendChild(element);
                    element = doc.CreateElement("boolVal");
                    element.InnerText = value.boolVal.ToString();
                    string xml = doc.OuterXml;
                }
            });

            TestHelper.Benchmark("binary", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    BinaryTable table = new BinaryTable();
                    table["intVal"] = value.intVal;
                    table["strVal"] = value.strVal;
                    table["floatVal"] = value.floatVal;
                    table["boolVal"] = value.boolVal;
                    byte[] bytes = BinaryParser.Encode(table);
                }
            });
        }

        [Test("Test BinaryParser vs XML deserialization")]
        public void Test_Deserialization()
        {
            StructForSerialize value = new StructForSerialize
            {
                intVal = 123,
                strVal = "asdasdasd",
                floatVal = 123.456f,
                boolVal = true,
            };

            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("root");
            doc.AppendChild(root);
            XmlElement element = doc.CreateElement("intVal");
            element.InnerText = value.intVal.ToString();
            root.AppendChild(element);
            element = doc.CreateElement("strVal");
            element.InnerText = value.strVal;
            root.AppendChild(element);
            element = doc.CreateElement("floatVal");
            element.InnerText = value.floatVal.ToString();
            root.AppendChild(element);
            element = doc.CreateElement("boolVal");
            element.InnerText = value.boolVal.ToString();
            root.AppendChild(element);
            string xml = doc.OuterXml;

            BinaryTable table = new BinaryTable();
            table["intVal"] = value.intVal;
            table["strVal"] = value.strVal;
            table["floatVal"] = value.floatVal;
            table["boolVal"] = value.boolVal;
            byte[] bytes = BinaryParser.Encode(table);

            StructForSerialize reuslt = default;

            

            TestHelper.Benchmark("xml", () =>
            {
                for (int i = 0; i < 100000; i++)
                {
                    doc.LoadXml(xml);
                    XmlElement root2 = doc.DocumentElement;

                    reuslt.intVal = int.Parse(root2["intVal"].InnerText);
                    reuslt.strVal = root2["strVal"].InnerText;
                    reuslt.floatVal = float.Parse(root2["floatVal"].InnerText);
                    reuslt.boolVal = bool.Parse(root2["boolVal"].InnerText);
                }
            });

            TestHelper.Benchmark("binary", () =>
            {
                for (int i = 0; i < 100000; i++)
                {
                    BinaryTable table2 = BinaryParser.Decode(bytes);
                    table2.TryGetValue("intVal", out reuslt.intVal);
                    table2.TryGetString("strVal", out reuslt.strVal);
                    table2.TryGetValue("floatVal", out reuslt.floatVal);
                    table2.TryGetValue("boolVal", out reuslt.boolVal);
                }
            });
        }

        [Test("Test BinaryParser vs XML size")]
        public void Test_Size()
        {
            StructForSerialize value = new StructForSerialize
            {
                intVal = 1222,
                strVal = "asdasdasd",
                floatVal = 123.456f,
                boolVal = true,
            };

            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("root");
            doc.AppendChild(root);
            XmlElement element = doc.CreateElement("intVal");
            element.InnerText = value.intVal.ToString();
            root.AppendChild(element);
            element = doc.CreateElement("strVal");
            element.InnerText = value.strVal;
            root.AppendChild(element);
            element = doc.CreateElement("floatVal");
            element.InnerText = value.floatVal.ToString();
            root.AppendChild(element);
            element = doc.CreateElement("boolVal");
            element.InnerText = value.boolVal.ToString();
            root.AppendChild(element);
            string xml = doc.OuterXml;
            long sizeXml = Encoding.UTF8.GetBytes(xml).Length;

            BinaryTable table = new BinaryTable();
            table["intVal"] = value.intVal;
            table["strVal"] = value.strVal;
            table["floatVal"] = value.floatVal;
            table["boolVal"] = value.boolVal;
            byte[] bytes = BinaryParser.Encode(table, out long sizeBinary);

            TestHelper.PrintBlue("xml: " + TestHelper.FormatSize(sizeXml));
            TestHelper.PrintBlue("binary: " + TestHelper.FormatSize(sizeBinary));
        }
    }
}

