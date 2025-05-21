using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Alco
{
    public enum BinaryValueType : byte
    {
        Value = 0x00,
        Array = 0x01,
        Table = 0x02,
    };

    public class BinaryValue : BaseBinaryValue
    {
        private readonly byte[] _binary;

        /// Properties
        public override BinaryValueType Type => BinaryValueType.Value;


        public byte[] Bytes
        {
            get
            {
                return _binary;
            }
        }

        public bool IsNull
        {
            get
            {
                return _binary.Length == 0;
            }
        }

        public BinaryValue()
        {
            _binary = Array.Empty<byte>();
        }


        public BinaryValue(byte[] v)
        {
            _binary = v;
        }

        public bool TryGetValue<T>(out T v) where T : unmanaged
        {
            v = UtilsBinary.DecodeToValue<T>(_binary);
            return true;
        }

        public bool TryGetNullableValue<T>(out T? v) where T : unmanaged
        {
            v = UtilsBinary.DecodeToNullableValue<T>(_binary);
            return true;
        }

        public bool TryGetEnum<T>(out T v) where T : struct, Enum
        {
            if (_binary.Length != Unsafe.SizeOf<T>())
            {
                v = default;
                return false;
            }

            v = Unsafe.ReadUnaligned<T>(ref _binary[0]);
            return true;
        }

        public bool TryGetString([NotNullWhen(true)] out string? v)
        {
            v = UtilsBinary.DecodeToString(_binary);
            return true;
        }

        public static BinaryValue CreateByValue<T>(T value) where T : unmanaged
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        public static BinaryValue CreateByNullableValue<T>(T? value) where T : unmanaged
        {
            return new BinaryValue(UtilsBinary.EncodeNullableValue(value));
        }

        public static BinaryValue CreateByEnum<T>(T value) where T : struct, Enum
        {
            return new BinaryValue(UtilsBinary.EncodeEnum(value));
        }

        public unsafe static BinaryValue CreateByMemory<T>(Span<T> memory) where T : unmanaged
        {
            fixed (T* ptrMemory = memory)
            {
                return new BinaryValue(new Span<byte>(ptrMemory, memory.Length * sizeof(T)).ToArray());
            }
        }
    }

}