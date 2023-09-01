using System;
using System.Runtime.CompilerServices;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

namespace Vocore
{
    public class BinaryParser
    {
        public static int SizeInt32 = Marshal.SizeOf<int>();

        public static byte[] Encode(BinaryTable data)
        {
            MemoryStream stream = new MemoryStream();
            EncodeTable(stream, data);

            return stream.GetBuffer();
        }

        public static BinaryTable Decode(byte[] bytes)
        {
            MemoryStream stream = new MemoryStream(bytes);
            BinaryReader reader = new BinaryReader(stream);
            return DecodeTable(reader);
        }

        private static void EncodeElement(MemoryStream stream, string name, BinaryValue v)
        {
            switch (v.Type)
            {
                case BinaryValueType.Null:
                    WriteType(stream, BinaryValueType.Null);
                    EncodeFieldName(stream, name);
                    return;
                case BinaryValueType.Binary:
                    WriteType(stream, BinaryValueType.Binary);
                    EncodeFieldName(stream, name);
                    EncodeBinary(stream, v.Bytes);
                    return;
                case BinaryValueType.Array:
                    WriteType(stream, BinaryValueType.Array);
                    EncodeFieldName(stream, name);
                    EncodeArray(stream, v as BinaryArray);
                    return;
                case BinaryValueType.Table:
                    WriteType(stream, BinaryValueType.Table);
                    EncodeFieldName(stream, name);
                    EncodeTable(stream, v as BinaryTable);
                    return;
            };
        }

        private static BinaryValue DecodeElement(BinaryReader reader, out string name)
        {
            BinaryValueType type = ReadType(reader);
            name = DecodeFieldName(reader);
            switch(type){
                case BinaryValueType.Null:
                    return new BinaryValue();
                case BinaryValueType.Binary:
                    return DecodeBinary(reader);
                case BinaryValueType.Array:
                    return DecodeArray(reader);
                case BinaryValueType.Table:
                    return DecodeTable(reader);
            }

            throw new Exception(string.Format("Don't know elementType={0}", type));
        }

        private static void EncodeTable(MemoryStream stream, BinaryTable table)
        {
            // using (MemoryStream tmpStream = new MemoryStream())
            // {
            //     foreach (string str in table.Keys)
            //     {
            //         EncodeElement(tmpStream, str, table[str]);
            //     }
            //     int length = (int)tmpStream.Position;

            //     WriteLength(stream, length);
            //     stream.Write(tmpStream.GetBuffer(), 0, length);
            // }

            int positionLength = (int)stream.Position;
            WriteLength(stream, 0);

            foreach (string str in table.Keys)
            {
                EncodeElement(stream, str, table[str]);
            }

            int positionEnd = (int)stream.Position;

            stream.Position = positionLength;
            WriteLength(stream, positionEnd - positionLength - SizeInt32);
            stream.Position = positionEnd;
        }

        private static BinaryTable DecodeTable(BinaryReader reader)
        {
            int length = ReadLength(reader);

            BinaryTable table = new BinaryTable();

            int i = (int)reader.BaseStream.Position;
            while (reader.BaseStream.Position < i + length - 1)
            {
                BinaryValue value = DecodeElement(reader, out string name);
                table.Add(name, value);
            }

            //reader.ReadByte(); // zero
            return table;
        }

        private static void EncodeArray(MemoryStream stream, BinaryArray lst)
        {

            using (MemoryStream tmpStream = new MemoryStream())
            {
                for(int i=0;i<lst.Count;i++)
                {
                    EncodeElement(tmpStream, Convert.ToString(i), lst[i]);
                }

                WriteLength(stream, (int)tmpStream.Position);
                stream.Write(tmpStream.GetBuffer(), 0, (int)tmpStream.Position);
            }
        }

        private static BinaryArray DecodeArray(BinaryReader reader)
        {

            BinaryArray array = new BinaryArray();
            int length = ReadLength(reader);

            int i = (int)reader.BaseStream.Position;
            while (reader.BaseStream.Position < i + length - 1)
            {
                BinaryValue value = DecodeElement(reader, out string _);
                array.Add(value);
            }

            //reader.ReadByte(); // zero

            return array;
        }

        private static void EncodeBinary(MemoryStream stream, byte[] buf)
        {
            WriteBinary(stream, buf);
        }

        private static BinaryValue DecodeBinary(BinaryReader reader)
        {
            return new BinaryValue(ReadBinary(reader));
        }

        private static void EncodeFieldName(MemoryStream stream, string v)
        {
            WriteString(stream, v);
        }

        private static string DecodeFieldName(BinaryReader reader)
        {
            return ReadString(reader);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteType(MemoryStream stream, BinaryValueType type)
        {
            stream.WriteByte((byte)type);
        }

        private static BinaryValueType ReadType(BinaryReader reader)
        {
            return (BinaryValueType)reader.ReadByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteLength(MemoryStream stream, int length)
        {
            stream.WriteInt32(length);
            //stream.WriteByte(0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadLength(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            //reader.ReadByte();
            return length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteBinary(MemoryStream stream, byte[] bytes)
        {
            WriteLength(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] ReadBinary(BinaryReader reader)
        {
            int length = ReadLength(reader);
            return reader.ReadBytes(length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteString(MemoryStream stream, string str)
        {
            byte[] bytes = UtilsBinary.FastStringToBytes(str);
            WriteBinary(stream, bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ReadString(BinaryReader reader)
        {
            byte[] bytes = ReadBinary(reader);
            return UtilsBinary.FastBytesToString(bytes);
        }
    }
}
