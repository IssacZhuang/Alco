using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public enum BinaryValueType : byte
    {
        Value = 0x01,
        Array = 0x02,
        Table = 0x03,
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

        public bool TryGetString([NotNullWhen(true)] out string? v)
        {
            v = UtilsBinary.DecodeToString(_binary);
            return true;
        }




    }

}