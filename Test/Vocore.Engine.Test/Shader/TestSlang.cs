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
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "shader.slang");
        var code = File.ReadAllText(path);

        SlangSession session = new();
        SlangCompileRequest request = session.CreateCompileRequest();
        request.SetCodeGenTarget(SlangCompileTarget.SLANG_SPIRV);
        int translationUnitIndex = request.AddTranslationUnit(SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG, "test_vertex.slang");
        request.AddTranslationUnitSourceString(translationUnitIndex, path, code);

        byte[] spirv = request.Compile();

        var reflection =  UtilsShaderRelfection.GetSpirvReflection(spirv);
        TestContext.WriteLine(reflection.ToString());
    }
}