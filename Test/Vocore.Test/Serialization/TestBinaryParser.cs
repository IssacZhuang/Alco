using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Vocore.Test
{
    public class TestBinaryParser
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

        [Test(Description = "Test BinaryParser object")]
        public void TestParseObject()
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
                table[item.Key] = item.Value;
            }

            string keySubData = "subData";
            BinaryTable subTable = new BinaryTable();
            foreach (var item in subData)
            {
                subTable[item.Key] = item.Value;
            }
            table[keySubData] = subTable;

            byte[] raw = BinaryParser.EncodeTable(table);

            BinaryTable table2 = BinaryParser.DecodeTable(raw);

            foreach (var item in data)
            {
                if (table2.TryGetString(item.Key, out string value))
                {
                    if (item.Value == null)
                    {
                        Assert.IsFalse(value != "");
                    }
                    else
                    {
                        Assert.IsFalse(value != item.Value);
                    }
                }
            }

            BinaryTable subTable2 = table2[keySubData] as BinaryTable;
            foreach (var item in subData)
            {
                // byte[] value = subTable2[item.Key].Bytes;
                // string str = Encoding.UTF8.GetString(value);
                // Assert.IsFalse(str != item.Value);
                if (subTable2.TryGetString(item.Key, out string value))
                {
                    // Assert.IsFalse(value != item.Value);
                    if (item.Value == null)
                    {
                        Assert.IsFalse(value != "");
                    }
                    else
                    {
                        Assert.IsFalse(value != item.Value);
                    }
                }
            }
        }

        [Test(Description = "Test BinaryParser list")]
        public void TestParseList()
        {
            // some random data
            string[] data = new string[5]{
                "value1",
                "value2",
                null, // equals to Array.Empty<byte>() or string.Empty
                "value4",
                "value5",
            };

            BinaryArray binArray = new BinaryArray();

            foreach (var item in data)
            {
                binArray.Add(item);
            }

            BinaryTable binObject = new BinaryTable();
            binObject["noise1"] = null;
            binObject["list"] = binArray;
            binObject["noise2"] = "noise";
            byte[] raw = BinaryParser.EncodeTable(binObject);

            BinaryTable binObject2 = BinaryParser.DecodeTable(raw);
            BinaryArray binArray2 = binObject2["list"] as BinaryArray;

            for (int i = 0; i < data.Length; i++)
            {
                // byte[] value = binArray2[i].Bytes;
                // string str = Encoding.UTF8.GetString(value);
                // Assert.IsFalse(str != data[i]);
                if (binArray2.TryGetString(i, out string value))
                {
                    if (data[i] == null)
                    {
                        Assert.IsFalse(value != "");
                    }
                    else
                    {
                        Assert.IsFalse(value != data[i]);
                    }
                }
            }
        }

        [Test(Description = "Test BinaryParser convert")]
        public void TestConvert()
        {
            BinaryTable table = new BinaryTable
            {
                ["key1"] = "value1",
                ["key2"] = "value2",
                ["key3"] = null, // equals to Array.Empty<byte>() or string.Empty
                ["key4"] = "value4"
            };

            byte[] raw = BinaryParser.EncodeTable(table);

            BinaryTable table2 = BinaryParser.DecodeTable(raw);

            Assert.IsTrue(table2.TryGetString("key1", out string value1));
            Assert.IsTrue(value1 == "value1");
            Assert.IsTrue(table2.TryGetString("key2", out string value2));
            Assert.IsTrue(value2 == "value2");
            Assert.IsTrue(table2.TryGetString("key3", out string value3));
            Assert.IsTrue(value3 == "");
            Assert.IsTrue(table2.TryGetString("key4", out string value4));
            Assert.IsTrue(value4 == "value4");
        }

        struct StructForSerialize
        {
            public int intVal;
            public string strVal;
            public float floatVal;
            public bool boolVal;
        }


        [Test(Description = "Test BinaryParser vs XML size")]
        public void TestSize()
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
            byte[] bytes = BinaryParser.EncodeTable(table);

            TestContext.WriteLine("xml: " + UtilsTest.FormatSize(sizeXml));
            TestContext.WriteLine("binary: " + UtilsTest.FormatSize(bytes.Length));
        }
    }
}

