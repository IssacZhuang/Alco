using BenchmarkDotNet.Attributes;
using System.Collections.Frozen;

namespace Alco.Benchmark;

/// <summary>
/// Benchmark tests for Dictionary and FrozenDictionary construction speed
/// </summary>
[MemoryDiagnoser]
public class BenchmarkDictionary
{
    [Params(10, 500, 10000)]
    public int Count;
    private List<Tuple<string, int>> _data;

    [GlobalSetup]
    public void Setup()
    {
        _data = new List<Tuple<string, int>>(Count);
        for (int i = 0; i < Count; i++)
        {
            _data.Add(Tuple.Create(i.ToString(), i));
        }
    }

    [Benchmark(Description = "Dictionary construction")]
    public Dictionary<string, int> ConstructDictionary()
    {
        var dict = new Dictionary<string, int>(_data.Count);
        foreach (var item in _data)
        {
            dict.Add(item.Item1, item.Item2);
        }
        return dict;
    }

    [Benchmark(Description = "FrozenDictionary construction")]
    public FrozenDictionary<string, int> ConstructFrozenDictionary()
    {
        return _data.ToFrozenDictionary(x => x.Item1, x => x.Item2);
    }
}

