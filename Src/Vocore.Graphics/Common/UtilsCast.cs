namespace Vocore.Graphics;

internal static class UtilsCast
{
    public static void GenerateCastTable<TA, TB>(IEnumerable<Tuple<TA, TB>> castList, out Dictionary<TA, TB> aToB, out Dictionary<TB, TA> bToA) where TA : notnull where TB : notnull
    {
        aToB = new Dictionary<TA, TB>();
        bToA = new Dictionary<TB, TA>();
        foreach (var (a, b) in castList)
        {
            aToB.Add(a, b);
            bToA.Add(b, a);
        }
    }

    public static Func<TA, TB> GenerateCastFunc<TA, TB>(Dictionary<TA, TB> castTable) where TA : notnull where TB : notnull
    {
        return a => castTable[a];
    }
}