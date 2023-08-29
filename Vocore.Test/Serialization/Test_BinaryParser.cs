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
                new KeyValuePair<string, string>("key2", "value2"),
                new KeyValuePair<string, string>("key3", "value3"),
                new KeyValuePair<string, string>("key4", null),
                new KeyValuePair<string, string>("key5", "value5"),
                new KeyValuePair<string, string>("key6", "value6"),
            };

            BinaryTable binObject = new BinaryTable();

            foreach (var item in data)
            {
                if (item.Value == null)
                {
                    binObject[item.Key] = BinaryValue.NullValue;
                    continue;
                }
                byte[] value = Encoding.UTF8.GetBytes(item.Value);
                binObject[item.Key] = value;
            }

            byte[] raw = BinaryParser.Encode(binObject);

            BinaryTable binObject2 = BinaryParser.Decode(raw);

            foreach (var item in data)
            {
                if (item.Value == null)
                {
                    TestHelper.AssertFalse(binObject2[item.Key].Type != BinaryValue.ValueType.Null);
                    continue;
                }
                byte[] value = binObject2[item.Key].Bytes;
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

            BinArray binArray = new BinArray();

            foreach (var item in data)
            {
                if (item == null)
                {
                    binArray.Add(BinaryValue.NullValue);
                    continue;
                }
                byte[] value = Encoding.UTF8.GetBytes(item);
                binArray.Add(value);
            }

            BinaryTable binObject = new BinaryTable();
            binObject["list"] = binArray;
            byte[] raw = BinaryParser.Encode(binObject);

            BinaryTable binObject2 = BinaryParser.Decode(raw);
            BinArray binArray2 = binObject2["list"] as BinArray;

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
    }
}

