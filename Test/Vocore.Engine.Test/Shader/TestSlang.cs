using NUnit.Framework;
using Vocore.Graphics;
using Vocore.Rendering;
using Vocore.ShaderCompiler;

using static SlangSharp.Slang;

using SlangSharp;

namespace Vocore.Engine.Test;

public class TestSlang
{
    [Test(Description = "Test slang compile")]
    public void TestCompile()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Slang", "shader.slang");
        var code = File.ReadAllText(path);


        SlangSession session = spCreateSession("test_session");
        SlangCompileRequest request = spCreateCompileRequest(session);

        spSetCodeGenTarget(request, SlangCompileTarget.SLANG_SPIRV);

        int translationUnitIndex = spAddTranslationUnit(request, SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG, "test_vertex.slang");
        spAddTranslationUnitSourceString(request, translationUnitIndex, path, code);

        SlangResult result = spCompile(request);
        if (result.IsError)
        {
            TestContext.WriteLine(request.GetDiagnosticString());
            Assert.Fail("Failed to compile");
        }

        SlangReflection reflection = spGetReflection(request);
        var entryCount = spReflection_getEntryPointCount(reflection);
        TestContext.WriteLine($"Entry count: {entryCount}");

        Assert.That(entryCount, Is.EqualTo(2));
    }

    [Test(Description = "Test slang spirv gen")]
    public void TestSpirvGen()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Slang", "shader.slang");
        var code = File.ReadAllText(path);

        SlangSession session = spCreateSession("test_session");
        SlangCompileRequest request = spCreateCompileRequest(session);

        //none for reflection
        spSetCodeGenTarget(request, SlangCompileTarget.SLANG_SPIRV);

        int translationUnitIndex = spAddTranslationUnit(request, SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG, "test_vertex.slang");
        spAddTranslationUnitSourceString(request, translationUnitIndex, path, code);
        SlangResult result = spCompile(request);

        TestContext.WriteLine(request.GetDiagnosticString());
        if (result.IsError)
        {
            
            Assert.Fail("Failed to compile");
        }

        SlangReflection reflection = spGetReflection(request);

        //get vertex name

        var entryCount = spReflection_getEntryPointCount(reflection);

        int vertexIndex = 0;
        int fragmentIndex = 0;
        for (uint i = 0; i < entryCount; i++)
        {
            SlangReflectionEntryPoint entry = spReflection_getEntryPointByIndex(reflection, i);
            if (spReflectionEntryPoint_getStage(entry) == SlangStage.SLANG_STAGE_VERTEX)
            {
                vertexIndex = (int)i;
                TestContext.WriteLine($"Vertex name: {entry.GetName()}");
            }
            else if (spReflectionEntryPoint_getStage(entry) == SlangStage.SLANG_STAGE_FRAGMENT)
            {
                fragmentIndex = (int)i;
                TestContext.WriteLine($"Fragment name:\n{entry.GetName()}");
            }
        }

        

        byte[] vertexSpirv = request.GetBytesByEntryPointIndex(vertexIndex);

        var vertexReflection = UtilsShaderRelfection.GetSpirvReflection(vertexSpirv);

        TestContext.WriteLine($"Vertex reflection:\n{vertexReflection}");

        byte[] fragmentSpirv = request.GetBytesByEntryPointIndex(fragmentIndex);

        var fragmentReflection = UtilsShaderRelfection.GetSpirvReflection(fragmentSpirv);

        TestContext.WriteLine($"Fragment reflection: {fragmentReflection}");
    }
}