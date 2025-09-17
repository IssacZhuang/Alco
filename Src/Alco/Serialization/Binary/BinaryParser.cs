using System;
using System.Runtime.CompilerServices;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

namespace Alco
{
    /// <summary>
    /// Provides functionality for serializing and deserializing objects to and from binary format.
    /// Handles conversion between C# objects, binary tables, and raw byte arrays.
    /// </summary>
    public class BinaryParser
    {
        /// <summary>
        /// Encodes a BinaryTable into a byte array.
        /// </summary>
        /// <param name="data">The BinaryTable to encode</param>
        /// <returns>A byte array containing the serialized data</returns>
        public static byte[] EncodeTable(BinaryTable data)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                EncodeTable(stream, data);
                return stream.GetBuffer();
            }
        }

        /// <summary>
        /// Decodes a byte array into a BinaryTable.
        /// </summary>
        /// <param name="bytes">The byte array to decode</param>
        /// <returns>A BinaryTable containing the deserialized data</returns>
        public static BinaryTable DecodeTable(byte[] bytes)
        {
            using (Stream stream = new MemoryStream(bytes))
            {
                return DecodeTable(stream);
            }
        }

        /// <summary>
        /// Decodes a ReadOnlySpan of bytes into a BinaryTable.
        /// </summary>
        /// <param name="bytes">The bytes to decode</param>
        /// <returns>A BinaryTable containing the deserialized data</returns>
        public unsafe static BinaryTable DecodeTable(ReadOnlySpan<byte> bytes)
        {
            fixed(byte* ptr = bytes)
            {
                using (Stream stream = new UnmanagedMemoryStream(ptr, bytes.Length))
                {
                    return DecodeTable(stream);
                }
            }
        }

        /// <summary>
        /// Converts an ISerializable object to a BinaryTable.
        /// </summary>
        /// <typeparam name="T">The type of the object, which must implement ISerializable</typeparam>
        /// <param name="obj">The object to convert</param>
        /// <returns>A BinaryTable representing the object's serialized state</returns>
        public static BinaryTable ObjectToTable<T>(T obj, Action<string>? onError = null) where T : ISerializable
        {
            ReferenceContext referenceContext = new ReferenceContext();
            BinarySerializeWriteNode node = new BinarySerializeWriteNode(referenceContext, onError);
            obj.OnSerialize(node, SerializeMode.Save);
            return node.Content;
        }

        /// <summary>
        /// Creates a new instance of type T and populates it from a BinaryTable.
        /// </summary>
        /// <typeparam name="T">The type to create, which must implement ISerializable and have a parameterless constructor</typeparam>
        /// <param name="content">The BinaryTable containing the serialized data</param>
        /// <returns>A new instance of T populated from the BinaryTable</returns>
        public static T TableToObject<T>(BinaryTable content, Action<string>? onError = null) where T : ISerializable, new()
        {
            ReferenceContext referenceContext = new ReferenceContext();
            T obj = new T();
            obj.OnSerialize(new BinarySerializeReadNode(referenceContext, content, onError), SerializeMode.Load);
            // Post-load pass to resolve references and finalize state
            obj.OnSerialize(new BinaryPostLoadSerializeNode(referenceContext, content, onError), SerializeMode.PostLoad);
            return obj;
        }

        /// <summary>
        /// Creates an instance of type T using a factory method and populates it from a BinaryTable.
        /// </summary>
        /// <typeparam name="T">The type to create, which must implement ISerializable</typeparam>
        /// <param name="content">The BinaryTable containing the serialized data</param>
        /// <param name="onCreate">A factory method that creates an instance of T</param>
        /// <returns>An instance of T populated from the BinaryTable</returns>
        public static T TableToObject<T>(BinaryTable content, Func<SerializeReadNode, T> onCreate, Action<string>? onError = null) where T : ISerializable
        {
            ReferenceContext referenceContext = new ReferenceContext();
            BinarySerializeReadNode node = new BinarySerializeReadNode(referenceContext, content, onError);
            T obj = onCreate(node);
            obj.OnSerialize(node, SerializeMode.Load);
            // Post-load pass to resolve references and finalize state
            obj.OnSerialize(new BinaryPostLoadSerializeNode(referenceContext, content, onError), SerializeMode.PostLoad);
            return obj;
        }

        /// <summary>
        /// Serializes an object to a byte array.
        /// </summary>
        /// <typeparam name="T">The type of the object, which must implement ISerializable</typeparam>
        /// <param name="obj">The object to serialize</param>
        /// <returns>A byte array containing the serialized object</returns>
        public static byte[] Encode<T>(T obj, Action<string>? onError = null) where T : ISerializable
        {
            return EncodeTable(ObjectToTable(obj, onError));
        }

        /// <summary>
        /// Deserializes a byte array into a new instance of type T.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to, which must implement ISerializable and have a parameterless constructor</typeparam>
        /// <param name="bytes">The byte array to deserialize</param>
        /// <returns>A new instance of T deserialized from the byte array</returns>
        public static T Decode<T>(byte[] bytes, Action<string>? onError = null) where T : ISerializable, new()
        {
            return TableToObject<T>(DecodeTable(bytes), onError);
        }

        /// <summary>
        /// Deserializes a ReadOnlySpan of bytes into a new instance of type T.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to, which must implement ISerializable and have a parameterless constructor</typeparam>
        /// <param name="bytes">The bytes to deserialize</param>
        /// <returns>A new instance of T deserialized from the bytes</returns>
        public static T Decode<T>(ReadOnlySpan<byte> bytes, Action<string>? onError = null) where T : ISerializable, new()
        {
            return TableToObject<T>(DecodeTable(bytes), onError);
        }

        /// <summary>
        /// Deserializes a byte array into an instance of type T created using a factory method.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to, which must implement ISerializable</typeparam>
        /// <param name="bytes">The byte array to deserialize</param>
        /// <param name="onCreate">A factory method that creates an instance of T</param>
        /// <returns>An instance of T deserialized from the byte array</returns>
        public static T Decode<T>(byte[] bytes, Func<SerializeReadNode, T> onCreate, Action<string>? onError = null) where T : ISerializable
        {
            return TableToObject<T>(DecodeTable(bytes), onCreate, onError);
        }

        /// <summary>
        /// Deserializes a ReadOnlySpan of bytes into an instance of type T created using a factory method.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to, which must implement ISerializable</typeparam>
        /// <param name="bytes">The bytes to deserialize</param>
        /// <param name="onCreate">A factory method that creates an instance of T</param>
        /// <returns>An instance of T deserialized from the bytes</returns>
        public static T Decode<T>(ReadOnlySpan<byte> bytes, Func<SerializeReadNode, T> onCreate, Action<string>? onError = null) where T : ISerializable
        {
            return TableToObject<T>(DecodeTable(bytes), onCreate, onError);
        }

        /// <summary>
        /// Deserializes a ReadOnlySpan of bytes into an existing instance of type T.
        /// This method populates the provided object with data from the byte array without creating a new instance.
        /// </summary>
        /// <typeparam name="T">The type of the object to populate, which must implement ISerializable</typeparam>
        /// <param name="bytes">The bytes to deserialize</param>
        /// <param name="obj">The existing object instance to populate with deserialized data</param>
        public static void Populate<T>(ReadOnlySpan<byte> bytes, T obj, Action<string>? onError = null) where T : ISerializable
        {
            ReferenceContext referenceContext = new ReferenceContext();
            BinaryTable content = DecodeTable(bytes);
            obj.OnSerialize(new BinarySerializeReadNode(referenceContext, content, onError), SerializeMode.Load);
            // Post-load pass to resolve references and finalize state
            obj.OnSerialize(new BinaryPostLoadSerializeNode(referenceContext, content, onError), SerializeMode.PostLoad);
        }


        private static void EncodeTableElement(Stream stream, string name, BaseBinaryValue value)
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

        private static BaseBinaryValue DecodeTableElement(Stream stream, out string name)
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

        private static void EncodeTable(Stream stream, BinaryTable table)
        {
            int positionLength = (int)stream.Position;
            WriteLength(stream, 0);


            foreach (var entry in table.Entries)
            {
                EncodeTableElement(stream, entry.Key, entry.Value);
            }

            int positionEnd = (int)stream.Position;

            stream.Position = positionLength;
            WriteLength(stream, positionEnd - positionLength - sizeof(int));
            stream.Position = positionEnd;
        }

        private static BinaryTable DecodeTable(Stream stream)
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

        private static void EncodeArrayElement(Stream stream, BaseBinaryValue value)
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

        private static BaseBinaryValue DecodeArrayElement(Stream stream)
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

        private static void EncodeArray(Stream stream, BinaryArray list)
        {
            int positionLength = (int)stream.Position;
            WriteLength(stream, 0);

            for (int i = 0; i < list.Count; i++)
            {
                EncodeArrayElement(stream, list[i]);
            }

            int positionEnd = (int)stream.Position;
            stream.Position = positionLength;
            WriteLength(stream, positionEnd - positionLength - sizeof(int));
            stream.Position = positionEnd;
        }

        private static BinaryArray DecodeArray(Stream stream)
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

        private static void EncodeBinary(Stream stream, byte[] bytes)
        {
            WriteBinary(stream, bytes);
        }

        private static BinaryValue DecodeBinary(Stream stream)
        {
            return new BinaryValue(ReadBinary(stream));
        }

        private static void EncodeFieldName(Stream stream, string value)
        {
            WriteString(stream, value);
        }

        private static string DecodeFieldName(Stream stream)
        {
            return ReadString(stream);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteType(Stream stream, BinaryValueType type)
        {
            stream.WriteByte((byte)type);
        }

        private static BinaryValueType ReadType(Stream stream)
        {
            return (BinaryValueType)stream.ReadByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteLength(Stream stream, int length)
        {
            stream.WriteInt32(length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadLength(Stream stream)
        {
            return stream.ReadInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteBinary(Stream stream, byte[] bytes)
        {
            WriteLength(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] ReadBinary(Stream stream)
        {
            int length = ReadLength(stream);
            return stream.ReadBytes(length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteString(Stream stream, string str)
        {
            byte[] bytes = UtilsBinary.FastStringToBytes(str);
            WriteBinary(stream, bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ReadString(Stream stream)
        {
            byte[] bytes = ReadBinary(stream);
            return UtilsBinary.FastBytesToString(bytes);
        }
    }
}
