using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Vocore
{
    public class ChunkList<T> : ICollection<T>
    {
        private readonly List<Chunk> _chunks;
        private int _count;
        public int Count => _count;
        public int ChunkSize => Chunk.MaxCount;
        public bool IsReadOnly => false;

        public ChunkList()
        {
            _chunks = new List<Chunk>();
            _count = 0;
        }

        public void Add(T element)
        {
            if (_count % Chunk.MaxCount == 0)
            {
                _chunks.Add(Chunk.Create());
            }

            _chunks[_chunks.Count - 1].Add(element);
            _count++;
        }

        public bool Remove(T element)
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                Chunk chunk = _chunks[i];
                for (int j = 0; j < chunk.Count; j++)
                {
                    if (chunk[j].Equals(element))
                    {
                        Remove(i, j);
                        return true;
                    }
                }
            }

            return false;
        }

        public bool Contains(T item)
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                Chunk chunk = _chunks[i];
                for (int j = 0; j < chunk.Count; j++)
                {
                    if (chunk[j].Equals(item))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void Clear()
        {
            _chunks.Clear();
            _count = 0;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {

        }

        private void Remove(int index)
        {
            if (index >= _count)
            {
                throw ExceptionCollection.OutOfRange;
            }

            int chunkIndex = index / Chunk.MaxCount;
            int elementIndex = index % Chunk.MaxCount;
            Remove(chunkIndex, elementIndex);
        }

        private void Remove(int chunkIndex, int elementIndex)
        {
            _chunks[chunkIndex].Remove(elementIndex);
            _count--;

            if (elementIndex == 0)
            {
                _chunks.RemoveAt(chunkIndex);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                Chunk chunk = _chunks[i];
                for (int j = 0; j < chunk.Count; j++)
                {
                    yield return chunk[j];
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        internal struct Chunk
        {
            public const int MemSize = 16 * 1024;
            public static readonly int MaxCount = GetMaxCount();

            private T[] _elements;
            private int _count;

            public int Count => _count;

            public T this[int index]
            {
                get
                {
                    return _elements[index];
                }
            }

            public static Chunk Create()
            {
                return new Chunk
                {
                    _elements = new T[MaxCount],
                    _count = 0
                };
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

            public void Remove(int index)
            {
                if (index >= _count)
                {
                    throw ExceptionCollection.OutOfRange;
                }

                if (index == _count - 1)
                {
                    RemoveTail();
                    return;
                }

                _elements[index] = RemoveTail();
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

