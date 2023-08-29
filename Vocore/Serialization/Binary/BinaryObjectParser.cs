using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vocore
{
    public class BinaryParser
    {
        private readonly MemoryStream _memoryStream;
        private readonly BinaryReader _binaryReader;
        private readonly BinaryWriter _binaryWriter;

        public static BinaryTable Decode(byte[] bytes)
        {
            BinaryParser parser = new BinaryParser(bytes);
            return parser.DecodeTable();
        }

        public static byte[] Encode(BinaryTable data)
        {

            BinaryParser parser = new BinaryParser();
            MemoryStream ms = new MemoryStream();

            parser.EncodeObject(ms, data);

            byte[] buf = new byte[ms.Position];
            ms.Seek(0, SeekOrigin.Begin);
            ms.Read(buf, 0, buf.Length);

            return buf;
        }

        private BinaryParser(byte[] bytes = null)
        {
            if (bytes != null)
            {
                _memoryStream = new MemoryStream(bytes);
            }
            else
            {
                _memoryStream = new MemoryStream();
            }
            _binaryReader = new BinaryReader(_memoryStream);
        }

        private BinaryValue DecodeElement(out string name)
        {
            byte elementType = _binaryReader.ReadByte();
            name = DecodeFieldName();
            if (elementType == 0x03)
            { // Document
                return DecodeTable();
            }
            else if (elementType == 0x04)
            { // Array
                return DecodeArray();
            }
            else if (elementType == 0x05)
            { // Binary
                int length = _binaryReader.ReadInt32();
                _binaryReader.ReadByte(); // zero

                return new BinaryValue(_binaryReader.ReadBytes(length));
            }
            else if (elementType == 0x0A)
            { // None
                return new BinaryValue();
            }

            throw new Exception(string.Format("Don't know elementType={0}", elementType));
        }

        private BinaryTable DecodeTable()
        {
            int length = _binaryReader.ReadInt32() - 4;

            BinaryTable obj = new BinaryTable();

            int i = (int)_binaryReader.BaseStream.Position;
            while (_binaryReader.BaseStream.Position < i + length - 1)
            {
                string name;
                BinaryValue value = DecodeElement(out name);
                obj.Add(name, value);

            }

            _binaryReader.ReadByte(); // zero
            return obj;
        }

        private BinArray DecodeArray()
        {
            BinaryTable obj = DecodeTable();

            int i = 0;
            BinArray array = new BinArray();
            while (obj.ContainsKey(Convert.ToString(i)))
            {
                array.Add(obj[Convert.ToString(i)]);

                i += 1;
            }

            return array;
        }

        private string DecodeFieldName()
        {
            var ms = new MemoryStream();
            while (true)
            {
                byte buf = _binaryReader.ReadByte();
                if (buf == 0)
                    break;
                ms.WriteByte(buf);
            }

            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Position);
        }


        private void EncodeElement(MemoryStream ms, string name, BinaryValue v)
        {
            switch (v.Type)
            {
                case BinaryValue.ValueType.Object:
                    ms.WriteByte(0x03);
                    EncodeFieldName(ms, name);
                    EncodeObject(ms, v as BinaryTable);
                    return;
                case BinaryValue.ValueType.Array:
                    ms.WriteByte(0x04);
                    EncodeFieldName(ms, name);
                    EncodeArray(ms, v as BinArray);
                    return;
                case BinaryValue.ValueType.Binary:
                    ms.WriteByte(0x05);
                    EncodeFieldName(ms, name);
                    EncodeBinary(ms, v.Bytes);
                    return;
                case BinaryValue.ValueType.Null:
                    ms.WriteByte(0x0A);
                    EncodeFieldName(ms, name);
                    return;
            };
        }

        private void EncodeObject(MemoryStream ms, BinaryTable obj)
        {

            MemoryStream dms = new MemoryStream();
            foreach (string str in obj.Keys)
            {
                EncodeElement(dms, str, obj[str]);
            }

            BinaryWriter bw = new BinaryWriter(ms);
            bw.Write((int)(dms.Position + 4 + 1));
            bw.Write(dms.GetBuffer(), 0, (int)dms.Position);
            bw.Write((byte)0);
        }

        private void EncodeArray(MemoryStream ms, BinArray lst)
        {

            var obj = new BinaryTable();
            for (int i = 0; i < lst.Count; ++i)
            {
                obj.Add(Convert.ToString(i), lst[i]);
            }

            EncodeObject(ms, obj);
        }

        private void EncodeBinary(MemoryStream ms, byte[] buf)
        {
            byte[] aBuf = BitConverter.GetBytes(buf.Length);
            ms.Write(aBuf, 0, aBuf.Length);
            ms.WriteByte(0);
            ms.Write(buf, 0, buf.Length);
        }

        private void EncodeFieldName(MemoryStream ms, string v)
        {
            byte[] buf = new UTF8Encoding().GetBytes(v);
            ms.Write(buf, 0, buf.Length);
            ms.WriteByte(0);
        }

    }
}
