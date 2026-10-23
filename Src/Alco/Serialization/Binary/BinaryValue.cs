using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// Identifies the payload kind of a binary serialized value.
    /// </summary>
    public enum BinaryValueType : byte
    {
        Value = 0x00,
        Array = 0x01,
        Table = 0x02,
    };

    /// <summary>
    /// A binary-encoded value that decodes into unmanaged primitives, enums, or strings.
    /// </summary>
    public class BinaryValue : BaseBinaryValue
    {
        private readonly ReadOnlyMemory<byte> _binary;

        /// Properties
        public override BinaryValueType Type => BinaryValueType.Value;


        public ReadOnlyMemory<byte> Bytes
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

        /// <summary>
        /// Initializes an empty binary value.
        /// </summary>
        public BinaryValue()
        {
            _binary = Array.Empty<byte>();
        }


        /// <summary>
        /// Initializes a binary value from an encoded payload.
        /// </summary>
        /// <param name="v">The encoded payload.</param>
        public BinaryValue(ReadOnlyMemory<byte> v)
        {
            _binary = v;
        }

        /// <summary>
        /// Decodes the payload as a value of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The unmanaged value type.</typeparam>
        /// <param name="v">The decoded value.</param>
        /// <returns>True if the payload was decoded.</returns>
        public bool TryGetValue<T>(out T v) where T : unmanaged
        {
            v = BinaryUtility.DecodeToValue<T>(_binary.Span);
            return true;
        }

        /// <summary>
        /// Attempts to decode the payload as a nullable value of type <typeparamref name="T"/>, failing when the payload size does not match.
        /// </summary>
        /// <typeparam name="T">The unmanaged value type.</typeparam>
        /// <param name="v">The decoded value, or null when decoding fails.</param>
        /// <returns>True if the payload was decoded.</returns>
        public bool TryGetNullableValue<T>(out T? v) where T : unmanaged
        {
            if (_binary.Length != Unsafe.SizeOf<T?>())
            {
                v = null;
                return false;
            }
            v = BinaryUtility.DecodeToNullableValue<T>(_binary.Span);
            return true;
        }

        /// <summary>
        /// Attempts to decode the payload as an enum value of type <typeparamref name="T"/>, failing when the payload size does not match.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="v">The decoded enum value.</param>
        /// <returns>True if the payload was decoded.</returns>
        public bool TryGetEnum<T>(out T v) where T : unmanaged, Enum
        {
            if (_binary.Length != Unsafe.SizeOf<T>())
            {
                v = default;
                return false;
            }

            v = BinaryUtility.DecodeToValue<T>(_binary.Span);
            return true;
        }

        /// <summary>
        /// Decodes the payload as a string.
        /// </summary>
        /// <param name="v">The decoded string.</param>
        /// <returns>True if the payload was decoded.</returns>
        public bool TryGetString([NotNullWhen(true)] out string? v)
        {
            v = BinaryUtility.DecodeToString(_binary.Span);
            return true;
        }

        /// <summary>
        /// Creates a binary value containing the encoded form of the specified value.
        /// </summary>
        /// <typeparam name="T">The unmanaged value type.</typeparam>
        /// <param name="value">The value to encode.</param>
        /// <returns>The encoded binary value.</returns>
        public static BinaryValue CreateByValue<T>(T value) where T : unmanaged
        {
            return new BinaryValue(BinaryUtility.EncodeValue(value));
        }

        /// <summary>
        /// Creates a binary value containing the encoded form of the specified nullable value.
        /// </summary>
        /// <typeparam name="T">The unmanaged value type.</typeparam>
        /// <param name="value">The nullable value to encode.</param>
        /// <returns>The encoded binary value.</returns>
        public static BinaryValue CreateByNullableValue<T>(T? value) where T : unmanaged
        {
            return new BinaryValue(BinaryUtility.EncodeNullableValue(value));
        }

        /// <summary>
        /// Creates a binary value containing the encoded form of the specified enum value.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="value">The enum value to encode.</param>
        /// <returns>The encoded binary value.</returns>
        public static BinaryValue CreateByEnum<T>(T value) where T : unmanaged, Enum
        {
            return new BinaryValue(BinaryUtility.EncodeEnum(value));
        }

        /// <summary>
        /// Creates a binary value containing the raw bytes of the specified memory.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type.</typeparam>
        /// <param name="memory">The memory to copy.</param>
        /// <returns>The encoded binary value.</returns>
        public unsafe static BinaryValue CreateByMemory<T>(Span<T> memory) where T : unmanaged
        {
            fixed (T* ptrMemory = memory)
            {
                return new BinaryValue(new Span<byte>(ptrMemory, memory.Length * sizeof(T)).ToArray());
            }
        }
    }

}