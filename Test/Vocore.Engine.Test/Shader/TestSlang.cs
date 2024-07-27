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
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "test_vertex.slang");
        var code = File.ReadAllText(path);

        SlangSession session = new();
        SlangCompileRequest request = session.CreateCompileRequest();
        request.SetCodeGenTarget(SlangCompileTarget.SLANG_HLSL);
        int translationUnitIndex = request.AddTranslationUnit(SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG, "test_vertex.slang");
        request.AddTranslationUnitSourceString(translationUnitIndex, path, code);

        string translatedCode = request.Compile();
        TestContext.WriteLine(translatedCode);
    }
}