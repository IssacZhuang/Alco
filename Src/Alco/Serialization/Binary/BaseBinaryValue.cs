using System;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// Base class for values stored in binary serialization tables and arrays.
    /// </summary>
    public abstract class BaseBinaryValue
    {
        /// <summary>
        /// Gets the kind of binary value.
        /// </summary>
        public abstract BinaryValueType Type { get; }

        /// <summary>
        /// Creates a binary value from a byte array.
        /// </summary>
        /// <param name="value">The bytes to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(byte[] value)
        {
            return new BinaryValue(value);
        }

        /// <summary>
        /// Creates a binary value from binary memory.
        /// </summary>
        /// <param name="value">The bytes to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(ReadOnlyMemory<byte> value)
        {
            return new BinaryValue(value);
        }

        /// <summary>
        /// Creates a binary value from a string; <c>null</c> becomes an empty value.
        /// </summary>
        /// <param name="value">The string to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(string value)
        {
            if (value == null)
            {
                return new BinaryValue();
            }
            return new BinaryValue(BinaryUtility.EncodeString(value));
        }

        /// <summary>
        /// Creates a binary value from a 32-bit signed integer.
        /// </summary>
        /// <param name="value">The value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(int value)
        {
            return new BinaryValue(BinaryUtility.EncodeValue(value));
        }

        /// <summary>
        /// Creates a binary value from a 32-bit unsigned integer.
        /// </summary>
        /// <param name="value">The value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(uint value)
        {
            return new BinaryValue(BinaryUtility.EncodeValue(value));
        }

        /// <summary>
        /// Creates a binary value from a 64-bit signed integer.
        /// </summary>
        /// <param name="value">The value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(long value)
        {
            return new BinaryValue(BinaryUtility.EncodeValue(value));
        }

        /// <summary>
        /// Creates a binary value from a 64-bit unsigned integer.
        /// </summary>
        /// <param name="value">The value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(ulong value)
        {
            return new BinaryValue(BinaryUtility.EncodeValue(value));
        }

        /// <summary>
        /// Creates a binary value from a single-precision float.
        /// </summary>
        /// <param name="value">The value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(float value)
        {
            return new BinaryValue(BinaryUtility.EncodeValue(value));
        }

        /// <summary>
        /// Creates a binary value from a double-precision float.
        /// </summary>
        /// <param name="value">The value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(double value)
        {
            return new BinaryValue(BinaryUtility.EncodeValue(value));
        }

        /// <summary>
        /// Creates a binary value from a boolean.
        /// </summary>
        /// <param name="value">The value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(bool value)
        {
            return new BinaryValue(BinaryUtility.EncodeValue(value));
        }

        /// <summary>
        /// Creates a binary value from an unsigned byte.
        /// </summary>
        /// <param name="value">The value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(byte value)
        {
            return new BinaryValue(BinaryUtility.EncodeValue(value));
        }

        /// <summary>
        /// Creates a binary value from a signed byte.
        /// </summary>
        /// <param name="value">The value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(sbyte value)
        {
            return new BinaryValue(BinaryUtility.EncodeValue(value));
        }

        /// <summary>
        /// Creates a binary value from a 16-bit signed integer.
        /// </summary>
        /// <param name="value">The value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(short value)
        {
            return new BinaryValue(BinaryUtility.EncodeValue(value));
        }

        /// <summary>
        /// Creates a binary value from a 16-bit unsigned integer.
        /// </summary>
        /// <param name="value">The value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator BaseBinaryValue(ushort value)
        {
            return new BinaryValue(BinaryUtility.EncodeValue(value));
        }
    }
}