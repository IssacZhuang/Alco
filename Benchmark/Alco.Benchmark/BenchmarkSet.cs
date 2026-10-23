using BenchmarkDotNet.Attributes;
using System.Collections.Generic;
using System.Linq;

namespace Alco.Benchmark;

/// <summary>
/// Benchmarks <see cref="HashSet{T}"/> and <see cref="SortedSet{T}"/> operations.
/// </summary>
[MemoryDiagnoser]
public class BenchmarkSet
{
    private const int Count = 1000;
    private int[] _data;
    private HashSet<int> _hashSet;
    private SortedSet<int> _sortedSet;

    /// <summary>
    /// Initializes the shuffled data and the pre-populated sets.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _data = Enumerable.Range(0, Count).ToArray();
        // Shuffle data to make it more realistic
        var rnd = new System.Random(42);
        for (int i = 0; i < _data.Length; i++)
        {
            int randomIndex = rnd.Next(i, _data.Length);
            (_data[i], _data[randomIndex]) = (_data[randomIndex], _data[i]);
        }

        _hashSet = new HashSet<int>(_data);
        _sortedSet = new SortedSet<int>(_data);
    }

    /// <summary>
    /// Benchmarks adding elements to a <see cref="HashSet{T}"/>.
    /// </summary>
    [Benchmark(Description = "HashSet Add")]
    public void HashSetAdd()
    {
        var set = new HashSet<int>();
        for (int i = 0; i < Count; i++)
        {
            set.Add(_data[i]);
        }
    }

    /// <summary>
    /// Benchmarks adding elements to a <see cref="SortedSet{T}"/>.
    /// </summary>
    [Benchmark(Description = "SortedSet Add")]
    public void SortedSetAdd()
    {
        var set = new SortedSet<int>();
        for (int i = 0; i < Count; i++)
        {
            set.Add(_data[i]);
        }
    }

    /// <summary>
    /// Benchmarks looking up elements in a <see cref="HashSet{T}"/>.
    /// </summary>
    [Benchmark(Description = "HashSet Contains")]
    public bool HashSetContains()
    {
        bool result = false;
        for (int i = 0; i < Count; i++)
        {
            result ^= _hashSet.Contains(i);
        }
        return result;
    }

    /// <summary>
    /// Benchmarks looking up elements in a <see cref="SortedSet{T}"/>.
    /// </summary>
    [Benchmark(Description = "SortedSet Contains")]
    public bool SortedSetContains()
    {
        bool result = false;
        for (int i = 0; i < Count; i++)
        {
            result ^= _sortedSet.Contains(i);
        }
        return result;
    }

    /// <summary>
    /// Benchmarks removing elements from a <see cref="HashSet{T}"/>.
    /// </summary>
    [Benchmark(Description = "HashSet Remove")]
    public void HashSetRemove()
    {
        var set = new HashSet<int>(_data);
        for (int i = 0; i < Count; i++)
        {
            set.Remove(_data[i]);
        }
    }

    /// <summary>
    /// Benchmarks removing elements from a <see cref="SortedSet{T}"/>.
    /// </summary>
    [Benchmark(Description = "SortedSet Remove")]
    public void SortedSetRemove()
    {
        var set = new SortedSet<int>(_data);
        for (int i = 0; i < Count; i++)
        {
            set.Remove(_data[i]);
        }
    }
}
