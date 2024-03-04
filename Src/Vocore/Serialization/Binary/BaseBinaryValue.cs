using System.Runtime.CompilerServices;

namespace Vocore
{
    public abstract class BaseBinaryValue
    {
        public abstract BinaryValueType Type { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(byte[] value)
        {
            return new BinaryValue(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(string value)
        {
            if (value == null)
            {
                return new BinaryValue();
            }
            return new BinaryValue(UtilsBinary.EncodeString(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(int value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(uint value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(long value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(ulong value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(float value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(double value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(bool value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(byte value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(sbyte value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(short value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(ushort value)
        {
            return new BinaryValue(UtilsBinary.EncodeValue(value));
        }
    }
}