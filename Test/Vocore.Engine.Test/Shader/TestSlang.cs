using NUnit.Framework;
using Vocore.Graphics;
using Vocore.Rendering;
using Vocore.ShaderCompiler;

using static SlangSharp.Slang;

using SlangSharp;

namespace Vocore.Engine.Test;

public class TestSlang
{
    [Test(Description = "Test slang")]
    public void Test()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "shader.slang");
        var code = File.ReadAllText(path);

        // SlangSession session = new();
        // SlangCompileRequest request = session.CreateCompileRequest();
        // request.SetCodeGenTarget(SlangCompileTarget.SLANG_SPIRV);
        // int translationUnitIndex = request.AddTranslationUnit(SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG, "test_vertex.slang");
        // request.AddTranslationUnitSourceString(translationUnitIndex, path, code);

        SlangSession session = spCreateSession("test_session");
        SlangCompileRequest request = spCreateCompileRequest(session);
        spSetCodeGenTarget(request, SlangCompileTarget.SLANG_SPIRV);
        int translationUnitIndex = spAddTranslationUnit(request, SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG, "test_vertex.slang");
        spAddTranslationUnitSourceString(request, translationUnitIndex, path, code);

        SlangResult result = spCompile(request);
        Assert.IsTrue(result.IsOk);

        IntPtr ptr = spGetCompileRequestCode(request, out nuint size);
        byte[] spirv = UtilsSlangInterop.GetData(ptr, size);

        var reflection =  UtilsShaderRelfection.GetSpirvReflection(spirv);
        TestContext.WriteLine(reflection.ToString());
    }
}