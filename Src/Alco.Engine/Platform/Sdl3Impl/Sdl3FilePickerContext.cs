

using SDL3;
using static Alco.UtilsMemory;

namespace Alco.Engine;

public class DialogFileFilter
{
    public string Name { get; }
    public string Pattern { get; }

    public DialogFileFilter(string name, string pattern)
    {
        Name = name;
        Pattern = pattern;
    }
}

