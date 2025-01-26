using System;
using System.Runtime.CompilerServices;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

namespace Alco
{
    public class BinaryParser
    {
        public readonly static int SizeInt32 = Marshal.SizeOf<int>();

        public static byte[] EncodeTable(BinaryTable data)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                EncodeTable(stream, data);
                return stream.GetBuffer();
            }
        }

        public static BinaryTable DecodeTable(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes))
            {
                return DecodeTable(stream);
            }
        }

        public static BinaryTable ObjectToTable<T>(T obj) where T : ISerializable
        {
            BinarySerializeWriteNode node = new BinarySerializeWriteNode();
            obj.OnSerialize(node, SerializeMode.Save);
            return node.Content;
        }

        public static T TableToObject<T>(BinaryTable content) where T : ISerializable, new()
        {
            T obj = new T();
            obj.OnSerialize(new BinarySerializeReadNode(content), SerializeMode.Load);
            return obj;
        }

        public static byte[] Encode<T>(T obj) where T : ISerializable
        {
            return EncodeTable(ObjectToTable(obj));
        }

        public static T Decode<T>(byte[] bytes) where T : ISerializable, new()
        {
            return TableToObject<T>(DecodeTable(bytes));
        }

        

        private static void EncodeTableElement(MemoryStream stream, string name, BaseBinaryValue value)
        {
            switch (value)
            {
                case BinaryValue binaryValue:
                    WriteType(stream, BinaryValueType.Value);
                    EncodeFieldName(stream, name);
                    EncodeBinary(stream, binaryValue.Bytes);
                    return;
                case BinaryArray array:
                    WriteType(stream, BinaryValueType.Array);
                    EncodeFieldName(stream, name);
                    EncodeArray(stream, array);
                    return;
                case BinaryTable table:
                    WriteType(stream, BinaryValueType.Table);
                    EncodeFieldName(stream, name);
                    EncodeTable(stream, table);
                    return;
                default:
                    throw new Exception(string.Format("Don't know elementType={0}", value.GetType().Name));
            };
        }

        private static BaseBinaryValue DecodeTableElement(MemoryStream stream, out string name)
        {
            BinaryValueType type = ReadType(stream);
            name = DecodeFieldName(stream);
            switch(type){
                case BinaryValueType.Value:
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
                EncodeTableElement(stream, entry.Key, entry.Value);
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
                BaseBinaryValue value = DecodeTableElement(stream, out string name);
                table.Add(name, value);
            }

            return table;
        }

        private static void EncodeArrayElement(MemoryStream stream, BaseBinaryValue value)
        {
            switch (value)
            {
                case BinaryValue binaryValue:
                    WriteType(stream, BinaryValueType.Value);
                    EncodeBinary(stream, binaryValue.Bytes);
                    return;
                case BinaryArray array:
                    WriteType(stream, BinaryValueType.Array);
                    EncodeArray(stream, array);
                    return;
                case BinaryTable table:
                    WriteType(stream, BinaryValueType.Table);
                    EncodeTable(stream, table);
                    return;
                default:
                    throw new Exception(string.Format("Don't know elementType={0}", value.GetType().Name));
            };
        }

        private static BaseBinaryValue DecodeArrayElement(MemoryStream stream)
        {
            BinaryValueType type = ReadType(stream);
            switch(type){
                case BinaryValueType.Value:
                    return DecodeBinary(stream);
                case BinaryValueType.Array:
                    return DecodeArray(stream);
                case BinaryValueType.Table:
                    return DecodeTable(stream);
            }

            throw new Exception(string.Format("Don't know elementType={0}", type));
        }

        private static void EncodeArray(MemoryStream stream, BinaryArray list)
        {
            int positionLength = (int)stream.Position;
            WriteLength(stream, 0);

            for (int i = 0; i < list.Count; i++)
            {
                EncodeArrayElement(stream, list[i]);
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
                BaseBinaryValue value = DecodeArrayElement(stream);
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
