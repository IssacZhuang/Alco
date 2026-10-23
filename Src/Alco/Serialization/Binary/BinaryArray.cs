using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace Alco
{
    /// <summary>
    /// A binary value array that stores an ordered sequence of binary values.
    /// </summary>
    public class BinaryArray : BaseBinaryValue, IEnumerable
    {

        private readonly List<BaseBinaryValue> _content = new List<BaseBinaryValue>();

        /// <summary>
        /// Initializes an empty array.
        /// </summary>
        public BinaryArray()
            : base()
        {
        }

        //
        // Indexer
        //
        public BaseBinaryValue this[int index]
        {
            get { return _content[index]; }
            set { _content[index] = value; }
        }
        /// <summary>
        /// Gets the number of elements in the array.
        /// </summary>
        public int Count { get { return _content.Count; } }

        public override BinaryValueType Type => BinaryValueType.Array;

        //
        // Methods
        //
        /// <summary>
        /// Appends a binary value.
        /// </summary>
        /// <param name="v">The value to append.</param>
        public void Add(BaseBinaryValue v)
        {
            _content.Add(v);
        }

        /// <summary>
        /// Tries to read a string value at an index.
        /// </summary>
        /// <param name="index">The zero-based element index.</param>
        /// <param name="value">The string value when found; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if the index exists and holds a string.</returns>
        public bool TryGetString(int index, [NotNullWhen(true)] out string? value)
        {
            if (index < _content.Count)
            {
                if (_content[index] is BinaryValue binaryValue)
                {
                    return binaryValue.TryGetString(out value);
                }
            }
            value = null;
            return false;
        }

        /// <summary>
        /// Tries to read an unmanaged value at an index.
        /// </summary>
        /// <typeparam name="T">The unmanaged value type.</typeparam>
        /// <param name="index">The zero-based element index.</param>
        /// <param name="value">The decoded value when found; otherwise <c>default</c>.</param>
        /// <returns><c>true</c> if the index exists and holds a compatible value.</returns>
        public bool TryGetValue<T>(int index, [NotNullWhen(true)] out T value) where T : unmanaged
        {
            if (index < _content.Count)
            {
                if (_content[index] is BinaryValue binaryValue)
                {
                    return binaryValue.TryGetValue(out value);
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Tries to read a nested table at an index.
        /// </summary>
        /// <param name="index">The zero-based element index.</param>
        /// <param name="value">The nested table when found; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if the index exists and holds a table.</returns>
        public bool TryGetTable(int index, [NotNullWhen(true)] out BinaryTable? value)
        {
            if (index < _content.Count)
            {
                if (_content[index] is BinaryTable binaryTable)
                {
                    value = binaryTable;
                    return true;
                }
            }
            value = null;
            return false;
        }

        /// <summary>
        /// Tries to read a nested array at an index.
        /// </summary>
        /// <param name="index">The zero-based element index.</param>
        /// <param name="value">The nested array when found; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if the index exists and holds an array.</returns>
        public bool TryGetArray(int index, [NotNullWhen(true)] out BinaryArray? value)
        {
            if (index < _content.Count)
            {
                if (_content[index] is BinaryArray binaryArray)
                {
                    value = binaryArray;
                    return true;
                }
            }
            value = null;
            return false;
        }

        /// <summary>
        /// Tries to read binary data at an index.
        /// </summary>
        /// <param name="index">The zero-based element index.</param>
        /// <param name="value">The binary data when found; otherwise empty.</param>
        /// <returns><c>true</c> if the index exists and holds binary data.</returns>
        public bool TryGetBinary(int index, [NotNullWhen(true)] out ReadOnlyMemory<byte> value)
        {
            if (index < _content.Count)
            {
                if (_content[index] is BinaryValue binaryValue)
                {
                    value = binaryValue.Bytes;
                    return true;
                }
            }
            value = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        /// <summary>
        /// Tries to read the raw binary value at an index.
        /// </summary>
        /// <param name="index">The zero-based element index.</param>
        /// <param name="value">The stored value when found; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if the index exists.</returns>
        public bool TryGetValue(int index, [NotNullWhen(true)] out BaseBinaryValue? value)
        {
            if (index < _content.Count)
            {
                value = _content[index];
                return true;
            }
            value = null;
            return false;
        }

        /// <summary>
        /// Returns the index of the first occurrence of a value.
        /// </summary>
        /// <param name="item">The value to locate.</param>
        /// <returns>The zero-based index, or -1 if the value is not found.</returns>
        public int IndexOf(BaseBinaryValue item)
        {
            return _content.IndexOf(item);
        }
        /// <summary>
        /// Inserts a binary value at the given index.
        /// </summary>
        /// <param name="index">The zero-based insertion index.</param>
        /// <param name="item">The value to insert.</param>
        public void Insert(int index, BaseBinaryValue item)
        {
            _content.Insert(index, item);
        }
        /// <summary>
        /// Removes the first occurrence of a value.
        /// </summary>
        /// <param name="v">The value to remove.</param>
        /// <returns><c>true</c> if the value was removed.</returns>
        public bool Remove(BaseBinaryValue v)
        {
            return _content.Remove(v);
        }
        /// <summary>
        /// Removes the element at the given index.
        /// </summary>
        /// <param name="index">The zero-based index of the element to remove.</param>
        public void RemoveAt(int index)
        {
            _content.RemoveAt(index);
        }
        /// <summary>
        /// Removes all elements from the array.
        /// </summary>
        public void Clear()
        {
            _content.Clear();
        }

        /// <summary>
        /// Determines whether the array contains a value.
        /// </summary>
        /// <param name="v">The value to locate.</param>
        /// <returns><c>true</c> if the value is present.</returns>
        public bool Contains(BaseBinaryValue v)
        {
            return _content.Contains(v);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _content.GetEnumerator();
        }
    }
}