using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Vocore
{
    public class ChunkList<T> : ICollection<T>
    {
        private Chunk _head;
        private Chunk _tail;
        private int _count;
        public int Count => _count;
        public int ChunkSize => Chunk.MaxCount;
        public bool IsReadOnly => false;

        public ChunkList()
        {
            _head = new Chunk();
            _tail = _head;
            _count = 0;
        }

        public void Add(T element)
        {
            if (_tail.Count >= Chunk.MaxCount)
            {
                Chunk newChunk = new Chunk();
                _tail.Next = newChunk;
                newChunk.Prev = _tail;
                _tail = newChunk;
            }

            _tail.Add(element);
            _count++;
        }

        public bool Remove(T element)
        {
            bool result = false;
            Chunk chunk = _head;
            while (chunk != null)
            {

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (chunk[i].Equals(element))
                    {
                        Remove(chunk, i);
                        return true;
                    }
                }

                chunk = chunk.Next;
            }
            return result;
        }

        public bool Contains(T item)
        {
            Chunk chunk = _head;
            while (chunk != null)
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (chunk[i].Equals(item))
                    {
                        return true;
                    }
                }

                chunk = chunk.Next;
            }

            return false;
        }

        public void Clear()
        {
            _head = new Chunk();
            _tail = _head;
            _count = 0;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            Chunk chunk = _head;
            while (chunk != null)
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    array[arrayIndex] = chunk[i];
                    arrayIndex++;
                }

                chunk = chunk.Next;
            }
        }

        private void Remove(Chunk chunk, int index)
        {
            if (_tail.Count == 0)
            {
                if (_tail.Next != null)
                {
                    _tail.Next.Prev = null;
                    _tail.Next = null;
                }

                _tail = _tail.Prev;
            }

            if (chunk == _tail && index == _tail.Count - 1)
            {
                _tail.RemoveTail();
                _count--;
                return;
            }

            chunk.Replace(index, _tail.RemoveTail());
            _count--;
        }

        public IEnumerator<T> GetEnumerator()
        {
            Chunk chunk = _head;
            while (chunk != null)
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    yield return chunk[i];
                }

                chunk = chunk.Next;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        internal class Chunk
        {
            public const int MemSize = 16 * 1024;
            public static readonly int MaxCount = GetMaxCount();

            private T[] _elements;
            private int _count;
            private Chunk _next;
            private Chunk _prev;

            public int Count => _count;

            public T this[int index]
            {
                get
                {
                    return _elements[index];
                }
            }

            public Chunk Next
            {
                get => _next;
                set => _next = value;
            }

            public Chunk Prev
            {
                get => _prev;
                set => _prev = value;
            }

            public Chunk()
            {
                _elements = new T[MaxCount];
                _count = 0;
            }

            public void Add(T element)
            {
                if (_count >= MaxCount)
                {
                    throw ExceptionCollection.Full;
                }

                _elements[_count] = element;
                _count++;
            }

            public T RemoveTail()
            {
                if (_count == 0)
                {
                    throw ExceptionCollection.Empty;
                }

                T reuslt = _elements[_count - 1];
                _elements[_count - 1] = default(T);
                _count--;
                return reuslt;
            }

            public void Replace(int index, T element)
            {
                if (index >= _count)
                {
                    throw ExceptionCollection.OutOfRange;
                }

                _elements[index] = element;
            }

            public static int GetMaxCount()
            {
                if (typeof(T).IsValueType)
                    return MemSize / Marshal.SizeOf(typeof(T));
                else
                    return MemSize / Marshal.SizeOf(typeof(void*));
            }
        }
    }
}

