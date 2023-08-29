using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vocore
{
    public class BinaryObjectParser
    {
        private MemoryStream _memoryStream;
        private BinaryReader _binaryReader;
        private BinaryWriter _binaryWriter;

        public static BinObject Load(byte[] buf)
        {
            BinaryObjectParser parser = new BinaryObjectParser(buf);

            return parser.DecodeObject();
        }

        public static byte[] Dump(BinObject obj)
        {

            BinaryObjectParser parser = new BinaryObjectParser();
            MemoryStream ms = new MemoryStream();

            parser.EncodeObject(ms, obj);

            byte[] buf = new byte[ms.Position];
            ms.Seek(0, SeekOrigin.Begin);
            ms.Read(buf, 0, buf.Length);

            return buf;
        }

        private BinaryObjectParser(byte[] buf = null)
        {
            if (buf != null)
            {
                _memoryStream = new MemoryStream(buf);
                _binaryReader = new BinaryReader(_memoryStream);
            }
            else
            {
                _memoryStream = new MemoryStream();
                _binaryWriter = new BinaryWriter(_memoryStream);
            }
        }

        private BinValue DecodeElement(out string name)
        {
            byte elementType = _binaryReader.ReadByte();
            name = DecodeFieldName();
            if (elementType == 0x03)
            { // Document
                return DecodeObject();

            }
            else if (elementType == 0x04)
            { // Array
                return DecodeArray();

            }
            else if (elementType == 0x05)
            { // Binary
                int length = _binaryReader.ReadInt32();
                byte binaryType = _binaryReader.ReadByte();

                return new BinValue(_binaryReader.ReadBytes(length));
            }
            else if (elementType == 0x0A)
            { // None
                return new BinValue();
            }

            throw new Exception(string.Format("Don't know elementType={0}", elementType));
        }

        private BinObject DecodeObject()
        {
            int length = _binaryReader.ReadInt32() - 4;

            BinObject obj = new BinObject();

            int i = (int)_binaryReader.BaseStream.Position;
            while (_binaryReader.BaseStream.Position < i + length - 1)
            {
                string name;
                BinValue value = DecodeElement(out name);
                obj.Add(name, value);

            }

            _binaryReader.ReadByte(); // zero
            return obj;
        }

        private BinArray DecodeArray()
        {
            BinObject obj = DecodeObject();

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
                byte buf = (byte)_binaryReader.ReadByte();
                if (buf == 0)
                    break;
                ms.WriteByte(buf);
            }

            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Position);
        }


        private void EncodeElement(MemoryStream ms, string name, BinValue v)
        {
            switch (v.Type)
            {
                case BinValue.ValueType.Object:
                    ms.WriteByte(0x03);
                    EncodeFieldName(ms, name);
                    EncodeObject(ms, v as BinObject);
                    return;
                case BinValue.ValueType.Array:
                    ms.WriteByte(0x04);
                    EncodeFieldName(ms, name);
                    EncodeArray(ms, v as BinArray);
                    return;
                case BinValue.ValueType.Binary:
                    ms.WriteByte(0x05);
                    EncodeFieldName(ms, name);
                    EncodeBinary(ms, v.BinaryValue);
                    return;
                case BinValue.ValueType.Null:
                    ms.WriteByte(0x0A);
                    EncodeFieldName(ms, name);
                    return;
            };
        }

        private void EncodeObject(MemoryStream ms, BinObject obj)
        {

            MemoryStream dms = new MemoryStream();
            foreach (string str in obj.Keys)
            {
                EncodeElement(dms, str, obj[str]);
            }

            BinaryWriter bw = new BinaryWriter(ms);
            bw.Write((Int32)(dms.Position + 4 + 1));
            bw.Write(dms.GetBuffer(), 0, (int)dms.Position);
            bw.Write((byte)0);
        }

        private void EncodeArray(MemoryStream ms, BinArray lst)
        {

            var obj = new BinObject();
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
