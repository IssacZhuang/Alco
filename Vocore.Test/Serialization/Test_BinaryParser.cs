using System;
using System.Collections.Generic;
using System.Text;

namespace Vocore.Test
{
    public class Test_BinaryParser
    {
        [Test("Test BinaryParser object")]
        public void Test_ParseObject()
        {
            // some random data
            KeyValuePair<string, string>[] data = new KeyValuePair<string, string>[6]{
                new KeyValuePair<string, string>("key1", "value1"),
                new KeyValuePair<string, string>("key2", "valu````~~e2"),
                new KeyValuePair<string, string>("key3", "val     ue3"),
                new KeyValuePair<string, string>("key4", null),
                new KeyValuePair<string, string>("key5", "valu......e5"),
                new KeyValuePair<string, string>("key6", "val^^&&ue6"),
            };

            KeyValuePair<string, string>[] subData = new KeyValuePair<string, string>[3]{
                new KeyValuePair<string, string>("key1", "asdasdasd"),
                new KeyValuePair<string, string>("key2", ""),
                new KeyValuePair<string, string>("key3", "s90-09-9=090"),
            };

            
            BinaryTable table = new BinaryTable();
            


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
                    TestHelper.AssertFalse(table2[item.Key].Type != BinaryValue.ValueType.Null);
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
                    TestHelper.AssertFalse(subTable2[item.Key].Type != BinaryValue.ValueType.Null);
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
            binObject["list"] = binArray;
            byte[] raw = BinaryParser.Encode(binObject);

            BinaryTable binObject2 = BinaryParser.Decode(raw);
            BinaryArray binArray2 = binObject2["list"] as BinaryArray;

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == null)
                {
                    TestHelper.AssertFalse(binArray2[i].Type != BinaryValue.ValueType.Null);
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
            BinaryTable table = new BinaryTable();
            table["key1"] = "value1";
            table["key2"] = "value2";
            table["key3"] = null;
            table["key4"] = "value4";

            byte[] raw = BinaryParser.Encode(table);

            BinaryTable table2 = BinaryParser.Decode(raw);

            TestHelper.PrintArray(UtilsBinary.EncodeString(table2["key2"]));
            TestHelper.PrintArray(UtilsBinary.EncodeString("value2"));
            TestHelper.AssertFalse(table2["key1"].Equals("value1"));
            TestHelper.AssertFalse(table2["key2"] == "value2");
            TestHelper.AssertFalse(table2["key3"] == null);
            TestHelper.AssertFalse(table2["key4"] == "value4");
        }
    }
}

