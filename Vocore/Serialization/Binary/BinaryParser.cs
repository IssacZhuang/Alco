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

        public static byte[] Encode(BinaryTable data, out long length)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                EncodeTable(stream, data);
                length = (int)stream.Position;
                // the length of the buffer is the capacity of the MemoryStream but not real length
                return stream.GetBuffer();
            }
        }

        public static byte[] Encode(BinaryTable data)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                EncodeTable(stream, data);
                return stream.GetBuffer();
            }
        }

        public static BinaryTable Decode(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes))
            {
                return DecodeTable(stream);
            }
        }

        private static void EncodeElement(MemoryStream stream, string name, BinaryValue value)
        {
            switch (value.Type)
            {
                case BinaryValueType.Null:
                    WriteType(stream, BinaryValueType.Null);
                    EncodeFieldName(stream, name);
                    return;
                case BinaryValueType.Binary:
                    WriteType(stream, BinaryValueType.Binary);
                    EncodeFieldName(stream, name);
                    EncodeBinary(stream, value.Bytes);
                    return;
                case BinaryValueType.Array:
                    WriteType(stream, BinaryValueType.Array);
                    EncodeFieldName(stream, name);
                    EncodeArray(stream, value as BinaryArray);
                    return;
                case BinaryValueType.Table:
                    WriteType(stream, BinaryValueType.Table);
                    EncodeFieldName(stream, name);
                    EncodeTable(stream, value as BinaryTable);
                    return;
            };
        }

        private static BinaryValue DecodeElement(MemoryStream stream, out string name)
        {
            BinaryValueType type = ReadType(stream);
            name = DecodeFieldName(stream);
            switch(type){
                case BinaryValueType.Null:
                    return new BinaryValue();
                case BinaryValueType.Binary:
                    return DecodeBinary(stream);
                case BinaryValueType.Array:
                    return DecodeArray(stream);
                case BinaryValueType.Table:
                    return DecodeTable(stream);
            }

            throw new Exception(string.Format("Don't know elementType={0}", type));
        }

        private static void EncodeTable(MemoryStream stream, BinaryTable table)
        {
            int positionLength = (int)stream.Position;
            WriteLength(stream, 0);

            // foreach (string str in table.Keys)
            // {
            //     EncodeElement(stream, str, table[str]);
            // }

            foreach (var entry in table.Entries)
            {
                EncodeElement(stream, entry.Key, entry.Value);
            }

            int positionEnd = (int)stream.Position;

            stream.Position = positionLength;
            WriteLength(stream, positionEnd - positionLength - SizeInt32);
            stream.Position = positionEnd;
        }

        private static BinaryTable DecodeTable(MemoryStream stream)
        {
            int length = ReadLength(stream);

            BinaryTable table = new BinaryTable();

            int i = (int)stream.Position;
            while (stream.Position < i + length - 1)
            {
                BinaryValue value = DecodeElement(stream, out string name);
                table.Add(name, value);
            }

            return table;
        }

        private static void EncodeArray(MemoryStream stream, BinaryArray list)
        {
            int positionLength = (int)stream.Position;
            WriteLength(stream, 0);

            for (int i = 0; i < list.Count; i++)
            {
                EncodeElement(stream, Convert.ToString(i), list[i]);
            }

            int positionEnd = (int)stream.Position;
            stream.Position = positionLength;
            WriteLength(stream, positionEnd - positionLength - SizeInt32);
            stream.Position = positionEnd;
        }

        private static BinaryArray DecodeArray(MemoryStream stream)
        {

            BinaryArray array = new BinaryArray();
            int length = ReadLength(stream);

            int i = (int)stream.Position;
            while (stream.Position < i + length - 1)
            {
                BinaryValue value = DecodeElement(stream, out string _);
                array.Add(value);
            }

            return array;
        }

        private static void EncodeBinary(MemoryStream stream, byte[] bytes)
        {
            WriteBinary(stream, bytes);
        }

        private static BinaryValue DecodeBinary(MemoryStream stream)
        {
            return new BinaryValue(ReadBinary(stream));
        }

        private static void EncodeFieldName(MemoryStream stream, string value)
        {
            WriteString(stream, value);
        }

        private static string DecodeFieldName(MemoryStream stream)
        {
            return ReadString(stream);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteType(MemoryStream stream, BinaryValueType type)
        {
            stream.WriteByte((byte)type);
        }

        private static BinaryValueType ReadType(MemoryStream stream)
        {
            return (BinaryValueType)stream.ReadByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteLength(MemoryStream stream, int length)
        {
            stream.WriteInt32(length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadLength(MemoryStream stream)
        {
            return stream.ReadInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteBinary(MemoryStream stream, byte[] bytes)
        {
            WriteLength(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] ReadBinary(MemoryStream stream)
        {
            int length = ReadLength(stream);
            return stream.ReadBytes(length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteString(MemoryStream stream, string str)
        {
            byte[] bytes = UtilsBinary.FastStringToBytes(str);
            WriteBinary(stream, bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ReadString(MemoryStream stream)
        {
            byte[] bytes = ReadBinary(stream);
            return UtilsBinary.FastBytesToString(bytes);
        }
    }
}
