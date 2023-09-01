using System;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public enum BinaryValueType : byte
    {
        Null = 0x00,
        Binary = 0x01,
        Array = 0x02,
        Table = 0x03,
    };

    public class BinaryValue
    {
        

        private readonly BinaryValueType _type;
        private readonly byte[] _binary;

        /// Properties
        public BinaryValueType Type { get { return _type; } }


        public byte[] Bytes
        {
            get
            {
                if (_type == BinaryValueType.Binary)
                {
                    return _binary;
                }
                throw new Exception(string.Format("Original type is {0}. Cannot convert from {0} to binary", _type));
            }
        }

        public bool IsNull
        {
            get { return _type == BinaryValueType.Null; }
        }



        protected BinaryValue(BinaryValueType valueType)
        {
            _type = valueType;
        }

        public BinaryValue()
        {
            _type = BinaryValueType.Null;
        }


        public BinaryValue(byte[] v)
        {
            _type = BinaryValueType.Binary;
            _binary = v;
        }

        public bool TryGetValue<T>(out T v) where T : unmanaged
        {
            if (_type == BinaryValueType.Binary)
            {
                v = UtilsBinary.DecodeToValue<T>(_binary);
                return true;
            }
            v = default;
            return false;
        }

        public bool TryGetString(out string v)
        {
            if (_type == BinaryValueType.Binary)
            {
                v = UtilsBinary.DecodeToString(_binary);
                return true;
            }
            v = default;
            return false;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(byte[] value)
        {
            return new BinaryValue(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(string value)
        {
            if (value == null)
            {
                return new BinaryValue();
            }
            return new BinaryValue(UtilsBinary.EncodeString(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(int value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(uint value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(long value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(ulong value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(float value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(double value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(bool value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(byte value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(sbyte value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(short value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(ushort value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

    }

}