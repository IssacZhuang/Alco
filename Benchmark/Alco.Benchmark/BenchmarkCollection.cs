using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Collections.Generic;
using System.Collections.Frozen;
using Alco.Unsafe;

namespace Alco.Benchmark;

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

    [GlobalCleanup]
    public void Cleanup()
    {
        nativeArrayList.Dispose();
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

public class BenchmarkCollectionAccess
{
    private const int Count = 1000;

    private int[] array;
    private Dictionary<int, int> dictionary;
    private FrozenDictionary<int, int> frozenDictionary;


    [GlobalSetup]
    public void Setup()
    {
        array = new int[Count];
        dictionary = new Dictionary<int, int>();

        // Initialize with same data
        for (int i = 0; i < Count; i++)
        {
            array[i] = i;
            dictionary[i] = i;
        }

        // Create frozen dictionary from regular dictionary
        frozenDictionary = dictionary.ToFrozenDictionary();
    }

    [Benchmark(Description = "Array access")]
    public int ArrayAccess()
    {
        int sum = 0;
        for (int i = 0; i < Count; i++)
        {
            sum += array[i];
        }
        return sum;
    }

    [Benchmark(Description = "Dictionary access")]
    public int DictionaryAccess()
    {
        int sum = 0;
        for (int i = 0; i < Count; i++)
        {
            sum += dictionary[i];
        }
        return sum;
    }

    [Benchmark(Description = "FrozenDictionary access")]
    public int FrozenDictionaryAccess()
    {
        int sum = 0;
        for (int i = 0; i < Count; i++)
        {
            sum += frozenDictionary[i];
        }
        return sum;
    }


}


