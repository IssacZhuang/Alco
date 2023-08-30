using System;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public class BinaryValue
    {
        public enum ValueType : byte
        {
            Null = 0x00,
            Binary = 0x01,
            Array = 0x02,
            Table = 0x03,
        };

        private readonly ValueType _type;
        private readonly byte[] _binary;

        /// Properties
        public ValueType Type { get { return _type; } }


        public byte[] Bytes
        {
            get
            {
                if (_type == ValueType.Binary)
                {
                    return _binary;
                }
                throw new Exception(string.Format("Original type is {0}. Cannot convert from {0} to binary", _type));
            }
        }

        public bool IsNull
        {
            get { return _type == ValueType.Null; }
        }



        protected BinaryValue(ValueType valueType)
        {
            _type = valueType;
        }

        public BinaryValue()
        {
            _type = ValueType.Null;
        }


        public BinaryValue(byte[] v)
        {
            _type = ValueType.Binary;
            _binary = v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BinaryValue(byte[] value)
        {
            return new BinaryValue(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator byte[](BinaryValue value)
        {
            if (value.IsNull)
            {
                return null;
            }
            return value.Bytes;
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
        public static implicit operator string(BinaryValue value)
        {
            if (value.IsNull)
            {
                return null;
            }
            return UtilsBinary.DecodeToString(value.Bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(BinaryValue value, string str)
        {
            Log.Info("BinaryValue == string");
            if (str is null)
            {
                return value is null || value.IsNull;
            }
            if (value is null)
            {
                return str == null;
            }
            Log.Info(1);
            if (value.Type == ValueType.Table || value.Type == ValueType.Array)
            {
                return false;
            }
            Log.Info(2, str == null);
            if (value.IsNull)
            {
                return str == null;
            }
            Log.Info(3, UtilsBinary.DecodeToString(value.Bytes) == str);
            return UtilsBinary.DecodeToString(value.Bytes).Equals(str);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(BinaryValue value, string str)
        {
            return !(value == str);
        }
    }

}