using System.Collections.Frozen;

namespace Alco.Graphics;

internal static class UtilsCast
{
    public static Func<TA, TB> GenerateCastFunc<TA, TB>(FrozenDictionary<TA, TB> castTable) where TA : notnull where TB : notnull
    {
        return (a) =>
        {
            if (castTable.TryGetValue(a, out var b))
            {
                return b;
            }
            throw new GraphicsException($"Cannot cast {typeof(TA).Name}:{a} to {typeof(TB).Name}");
        };
    }
    
    // Tuple
    public static void GenerateCastTable<TA, TB>(IEnumerable<Tuple<TA, TB>> castList, out FrozenDictionary<TA, TB> aToB, out FrozenDictionary<TB, TA> bToA) where TA : notnull where TB : notnull
    {
        Dictionary<TA, TB> tmpAToB = new Dictionary<TA, TB>();
        Dictionary<TB, TA> tmpBToA = new Dictionary<TB, TA>();
        foreach (var (a, b) in castList)
        {
            tmpAToB.Add(a, b);
            tmpBToA.Add(b, a);
        }
        aToB = tmpAToB.ToFrozenDictionary();
        bToA = tmpBToA.ToFrozenDictionary();
    }

    public static Func<TA, TB> GenerateCastFunc<TA, TB>(IEnumerable<Tuple<TA, TB>> castList) where TA : notnull where TB : notnull
    {
        Dictionary<TA, TB> castTable = new Dictionary<TA, TB>();
        foreach (var (a, b) in castList)
        {
            castTable.Add(a, b);
        }

        return (a) =>
        {
            if (castTable.TryGetValue(a, out var b))
            {
                return b;
            }
            throw new GraphicsException($"Cannot cast {typeof(TA).Name}:{a} to {typeof(TB).Name}");
        };

    }


    public static void GenerateCastFunc<TA, TB>(IEnumerable<Tuple<TA, TB>> castList, out Func<TA, TB> aToB, out Func<TB, TA> bToA) where TA : notnull where TB : notnull
    {
        GenerateCastTable(castList, out var aToBTable, out var bToATable);
        aToB = GenerateCastFunc(aToBTable);
        bToA = GenerateCastFunc(bToATable);
    }

    //ValueTuple
    public static void GenerateCastTable<TA, TB>(IEnumerable<ValueTuple<TA, TB>> castList, out FrozenDictionary<TA, TB> aToB, out FrozenDictionary<TB, TA> bToA) where TA : notnull where TB : notnull
    {
        Dictionary<TA, TB> tmpAToB = new Dictionary<TA, TB>();
        Dictionary<TB, TA> tmpBToA = new Dictionary<TB, TA>();
        foreach (var (a, b) in castList)
        {
            tmpAToB.Add(a, b);
            tmpBToA.Add(b, a);
        }
        aToB = tmpAToB.ToFrozenDictionary();
        bToA = tmpBToA.ToFrozenDictionary();
    }

    public static Func<TA, TB> GenerateCastFunc<TA, TB>(IEnumerable<ValueTuple<TA, TB>> castList) where TA : notnull where TB : notnull
    {
        Dictionary<TA, TB> castTable = new Dictionary<TA, TB>();
        foreach (var (a, b) in castList)
        {
            castTable.Add(a, b);
        }

        return (a) =>
        {
            if (castTable.TryGetValue(a, out var b))
            {
                return b;
            }
            throw new GraphicsException($"Cannot cast {typeof(TA).Name}:{a} to {typeof(TB).Name}");
        };

    }

    public static void GenerateCastFunc<TA, TB>(IEnumerable<ValueTuple<TA, TB>> castList, out Func<TA, TB> aToB, out Func<TB, TA> bToA) where TA : notnull where TB : notnull
    {
        GenerateCastTable(castList, out var aToBTable, out var bToATable);
        aToB = GenerateCastFunc(aToBTable);
        bToA = GenerateCastFunc(bToATable);
    }
}