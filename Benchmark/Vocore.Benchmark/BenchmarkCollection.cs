using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Collections.Generic;
using Vocore.Unsafe;

namespace Vocore.Benchmark
{
    public class BenchmarkCollectionAdd
    {
        private const int Count = 1000;
        private List<int> list;
        private ChunkList<int> chunkList;
        private NativeArrayList<int> nativeArrayList;

        [GlobalSetup]
        public void Setup()
        {
            list = new List<int>();
            chunkList = new ChunkList<int>();
            nativeArrayList = new NativeArrayList<int>();
        }

        [Benchmark(Description = "List add")]
        public void List()
        {
            for (int i = 0; i < Count; i++)
            {
                list.Add(i);
            }
        }

        [Benchmark(Description = "ChunkList add")]
        public void ChunkList()
        {
            for (int i = 0; i < Count; i++)
            {
                chunkList.Add(i);
            }
        }

        [Benchmark(Description = "NativeArrayList add")]
        public void NativeArrayList()
        {
            for (int i = 0; i < Count; i++)
            {
                nativeArrayList.Add(i);
            }
        }

    }

    public class BenchmarkCollectionRemove
    {
        private const int Count = 1000;
        private List<int> list;
        private ChunkList<int> chunkList;
        private PriorityList<int> priorityList;
        private NativeArrayList<int> nativeArrayList;
        private MiniHeap<int> miniHeap;

        [GlobalSetup]
        public unsafe void Setup()
        {
            list = new List<int>();
            chunkList = new ChunkList<int>();
            priorityList = new PriorityList<int>();
            nativeArrayList = new NativeArrayList<int>();
            miniHeap = new MiniHeap<int>();

            for (int i = 0; i < Count; i++)
            {
                list.Add(i);
                chunkList.Add(i);
                priorityList.Add(i);
                nativeArrayList.Add(i);
                miniHeap.Alloc(i);
            }
        }

        [Benchmark(Description = "List remove")]
        public void List()
        {
            for (int i = 0; i < Count; i++)
            {
                list.Remove(i);
            }
        }

        [Benchmark(Description = "ChunkList remove")]
        public void ChunkList()
        {
            for (int i = 0; i < Count; i++)
            {
                chunkList.Remove(i);
            }
        }

        [Benchmark(Description = "NativeArrayList remove")]
        public void NativeArrayList()
        {
            for (int i = 0; i < Count; i++)
            {
                nativeArrayList.Remove(i);
            }
        }

    }
}

