using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Runtime.InteropServices;
using Alco;

namespace Alco.Benchmark;

/// <summary>
/// Benchmarks adding 1000 integers to a List and a NativeArrayList.
/// </summary>
public class BenchmarkCollectionAdd
{
    private const int Count = 1000;
    private List<int> list;
    private NativeArrayList<int> nativeArrayList;

    /// <summary>
    /// Creates the collections used by the benchmarks.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        list = new List<int>();
        nativeArrayList = new NativeArrayList<int>();
    }

    /// <summary>
    /// Disposes the native list.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        nativeArrayList.Dispose();
    }

    /// <summary>
    /// Benchmarks adding 1000 integers to a List.
    /// </summary>
    [Benchmark(Description = "List add")]
    public void List()
    {
        for (int i = 0; i < Count; i++)
        {
            list.Add(i);
        }
    }


    /// <summary>
    /// Benchmarks adding 1000 integers to a NativeArrayList.
    /// </summary>
    [Benchmark(Description = "NativeArrayList add")]
    public void NativeArrayList()
    {
        for (int i = 0; i < Count; i++)
        {
            nativeArrayList.Add(i);
        }
    }

}

/// <summary>
/// Benchmarks removing 1000 integers from a List and a NativeArrayList.
/// </summary>
public class BenchmarkCollectionRemove
{
    private const int Count = 1000;
    private List<int> list;
    private PriorityList<int> priorityList;
    private NativeArrayList<int> nativeArrayList;
    private MiniHeap<int> miniHeap;

    /// <summary>
    /// Creates and fills the collections used by the benchmarks.
    /// </summary>
    [GlobalSetup]
    public unsafe void Setup()
    {
        list = new List<int>();
        priorityList = new PriorityList<int>();
        nativeArrayList = new NativeArrayList<int>();
        miniHeap = new MiniHeap<int>();

        for (int i = 0; i < Count; i++)
        {
            list.Add(i);
            priorityList.Add(i);
            nativeArrayList.Add(i);
            miniHeap.Alloc(i);
        }
    }

    /// <summary>
    /// Benchmarks removing 1000 integers from a List.
    /// </summary>
    [Benchmark(Description = "List remove")]
    public void List()
    {
        for (int i = 0; i < Count; i++)
        {
            list.Remove(i);
        }
    }

    /// <summary>
    /// Benchmarks removing 1000 integers from a NativeArrayList.
    /// </summary>
    [Benchmark(Description = "NativeArrayList remove")]
    public void NativeArrayList()
    {
        for (int i = 0; i < Count; i++)
        {
            nativeArrayList.Remove(i);
        }
    }

}

/// <summary>
/// Benchmark tests for different collection access patterns
/// </summary>
public class BenchmarkCollectionAccess
{
    private const int Count = 1000;

    private int[] array;
    private Dictionary<int, int> dictionary;
    private FrozenDictionary<int, int> frozenDictionary;
    private unsafe int* unmanagedPointer;

    /// <summary>
    /// Initializes the arrays, dictionaries, and unmanaged buffer used by the benchmarks.
    /// </summary>
    [GlobalSetup]
    public unsafe void Setup()
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

        // Allocate unmanaged memory
        unmanagedPointer = (int*)NativeMemory.Alloc((nuint)(Count * sizeof(int)));

        // Initialize unmanaged memory with same data
        for (int i = 0; i < Count; i++)
        {
            unmanagedPointer[i] = i;
        }
    }

    /// <summary>
    /// Frees the unmanaged buffer.
    /// </summary>
    [GlobalCleanup]
    public unsafe void Cleanup()
    {
        // Free unmanaged memory
        if (unmanagedPointer != null)
        {
            NativeMemory.Free(unmanagedPointer);
            unmanagedPointer = null;
        }
    }

    /// <summary>
    /// Benchmarks reading 1000 elements from an array.
    /// </summary>
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

    /// <summary>
    /// Benchmarks reading 1000 elements from a Dictionary.
    /// </summary>
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

    /// <summary>
    /// Benchmarks reading 1000 elements from a FrozenDictionary.
    /// </summary>
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

    /// <summary>
    /// Benchmarks reading 1000 elements through a Span.
    /// </summary>
    [Benchmark(Description = "Span access")]
    public int SpanAccess()
    {
        Span<int> span = array.AsSpan();
        int sum = 0;
        for (int i = 0; i < Count; i++)
        {
            sum += span[i];
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks reading 1000 elements through an unmanaged pointer.
    /// </summary>
    [Benchmark(Description = "Unsafe pointer access")]
    public unsafe int UnsafePointerAccess()
    {
        int sum = 0;
        for (int i = 0; i < Count; i++)
        {
            sum += unmanagedPointer[i];
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks writing 1000 elements to an array.
    /// </summary>
    [Benchmark(Description = "Array write")]
    public void ArrayWrite()
    {
        for (int i = 0; i < Count; i++)
        {
            array[i] = i * 2;
        }
    }

    /// <summary>
    /// Benchmarks writing 1000 elements to a Dictionary.
    /// </summary>
    [Benchmark(Description = "Dictionary write")]
    public void DictionaryWrite()
    {
        for (int i = 0; i < Count; i++)
        {
            dictionary[i] = i * 2;
        }
    }

    /// <summary>
    /// Benchmarks writing 1000 elements through a Span.
    /// </summary>
    [Benchmark(Description = "Span write")]
    public void SpanWrite()
    {
        Span<int> span = array.AsSpan();
        for (int i = 0; i < Count; i++)
        {
            span[i] = i * 2;
        }
    }

    /// <summary>
    /// Benchmarks writing 1000 elements through an unmanaged pointer.
    /// </summary>
    [Benchmark(Description = "Unsafe pointer write")]
    public unsafe void UnsafePointerWrite()
    {
        for (int i = 0; i < Count; i++)
        {
            unmanagedPointer[i] = i * 2;
        }
    }
}


