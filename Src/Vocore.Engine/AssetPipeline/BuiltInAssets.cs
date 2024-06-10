

using System;
using Vocore.IO;
using Vocore.Rendering;

namespace Vocore.Engine;

public partial class BuiltInAssets
{
    private readonly AssetSystem _system;
    public BuiltInAssets(AssetSystem system)
    {
        _system = system;
    }

    private Shader GetShader(string path)
    {
        return _system.Load<Shader>(path);
    }

    private Font GetFont(string path)
    {
        return _system.Load<Font>(path);
    }

    // the rest parts is auto generated 
}

