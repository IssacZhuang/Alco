using System;
using System.Runtime.CompilerServices;
using System.IO;
using System.Text;

namespace Vocore
{
    public class BinaryParser
    {
        public static byte TypeNull = (byte)BinaryValue.ValueType.Null;
        public static byte TypeBinary = (byte)BinaryValue.ValueType.Binary;
        public static byte TypeArray = (byte)BinaryValue.ValueType.Array;
        public static byte TypeTable = (byte)BinaryValue.ValueType.Table;

        public static byte[] Encode(BinaryTable data)
        {
            MemoryStream stream = new MemoryStream();
            EncodeTable(stream, data);

            byte[] buf = new byte[stream.Position];
            stream.Seek(0, SeekOrigin.Begin);
            stream.Read(buf, 0, buf.Length);

            return buf;
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
                case BinaryValue.ValueType.Null:
                    stream.WriteByte(TypeNull);
                    EncodeFieldName(stream, name);
                    return;
                case BinaryValue.ValueType.Binary:
                    stream.WriteByte(TypeBinary);
                    EncodeFieldName(stream, name);
                    EncodeBinary(stream, v.Bytes);
                    return;
                case BinaryValue.ValueType.Array:
                    stream.WriteByte(TypeArray);
                    EncodeFieldName(stream, name);
                    EncodeArray(stream, v as BinaryArray);
                    return;
                case BinaryValue.ValueType.Table:
                    stream.WriteByte(TypeTable);
                    EncodeFieldName(stream, name);
                    EncodeTable(stream, v as BinaryTable);
                    return;
            };
        }

        private static BinaryValue DecodeElement(BinaryReader reader, out string name)
        {
            byte elementType = reader.ReadByte();
            name = DecodeFieldName(reader);
            if (elementType == TypeNull)
            {
                // None
                return new BinaryValue();
            }
            else if (elementType == TypeBinary)
            {
                // Binary
                return DecodeBinary(reader);
            }
            else if (elementType == TypeArray)
            {
                // Array
                return DecodeArray(reader);
            }
            else if (elementType == TypeTable)
            {
                // Document
                return DecodeTable(reader);
            }

            throw new Exception(string.Format("Don't know elementType={0}", elementType));
        }

        private static void EncodeTable(MemoryStream stream, BinaryTable table)
        {
            using (MemoryStream tmpStream = new MemoryStream())
            {
                foreach (string str in table.Keys)
                {
                    EncodeElement(tmpStream, str, table[str]);
                }

                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write((int)(tmpStream.Position + 4 + 1));
                writer.Write(tmpStream.GetBuffer(), 0, (int)tmpStream.Position);
                writer.Write((byte)0);
            }
        }

        private static BinaryTable DecodeTable(BinaryReader reader)
        {
            int length = reader.ReadInt32() - 4;

            BinaryTable table = new BinaryTable();

            int i = (int)reader.BaseStream.Position;
            while (reader.BaseStream.Position < i + length - 1)
            {
                BinaryValue value = DecodeElement(reader, out string name);
                table.Add(name, value);

            }

            reader.ReadByte(); // zero
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

                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write((int)(tmpStream.Position + 4 + 1));
                writer.Write(tmpStream.GetBuffer(), 0, (int)tmpStream.Position);
                writer.Write((byte)0);
            }
        }

        private static BinaryArray DecodeArray(BinaryReader reader)
        {

            BinaryArray array = new BinaryArray();
            int length = reader.ReadInt32() - 4;

            int i = (int)reader.BaseStream.Position;
            while (reader.BaseStream.Position < i + length - 1)
            {
                BinaryValue value = DecodeElement(reader, out string _);
                array.Add(value);
            }

            reader.ReadByte(); // zero

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
            // byte[] buf = Encoding.UTF8.GetBytes(v);
            // stream.Write(buf, 0, buf.Length);
            // stream.WriteByte(0);
            WriteString(stream, v);
        }

        private static string DecodeFieldName(BinaryReader reader)
        {
            // using (MemoryStream stream = new MemoryStream())
            // {
            //     while (true)
            //     {
            //         byte buf = reader.ReadByte();
            //         if (buf == 0)
            //             break;
            //         stream.WriteByte(buf);
            //     }

            //     return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Position);
            // }
            return ReadString(reader);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteLength(MemoryStream stream, int length)
        {
            byte[] buf = BitConverter.GetBytes(length);
            stream.Write(buf, 0, buf.Length);
            stream.WriteByte(0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadLength(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            reader.ReadByte();
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
