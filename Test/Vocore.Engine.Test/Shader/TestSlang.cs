using NUnit.Framework;
using Vocore.Graphics;
using Vocore.Rendering;
using Vocore.ShaderCompiler;

using SlangSharp;

namespace Vocore.Engine.Test;

public class TestSlang
{
    [Test(Description = "Test slang")]
    public void Test()
    {
        nint session = Slang.spCreateSession("test");
    }
}