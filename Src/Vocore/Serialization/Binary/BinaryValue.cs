using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Vocore
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

        public bool TryGetString([NotNullWhen(true)] out string? v)
        {
            v = UtilsBinary.DecodeToString(_binary);
            return true;
        }

        public static BinaryValue CreateValue<T>(T value) where T : unmanaged
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        public static BinaryValue CreateValueNullable<T>(T? value) where T : unmanaged
        {
            return new BinaryValue(UtilsBinary.EncodeNullableValue(value));
        }
    }

}