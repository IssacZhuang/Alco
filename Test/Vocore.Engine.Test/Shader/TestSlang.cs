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

        if (result.IsError)
        {
            TestContext.WriteLine(request.GetDiagnosticString());
            Assert.Fail("Failed to compile");
        }

        SlangReflection reflection = spGetReflection(request);

        //get vertex name

        var entryCount = spReflection_getEntryPointCount(reflection);

        string vertexName = "";
        for (uint i = 0; i < entryCount; i++)
        {
            SlangReflectionEntryPoint entry = spReflection_getEntryPointByIndex(reflection, i);
            if (spReflectionEntryPoint_getStage(entry) == SlangStage.SLANG_STAGE_VERTEX)
            {
                vertexName = entry.GetName();
                TestContext.WriteLine($"Vertex name: {vertexName}");
            }
        }

        //get fragment name
        string fragmentName = "";
        for (uint i = 0; i < entryCount; i++)
        {
            SlangReflectionEntryPoint entry = spReflection_getEntryPointByIndex(reflection, i);
            if (spReflectionEntryPoint_getStage(entry) == SlangStage.SLANG_STAGE_FRAGMENT)
            {
                fragmentName = entry.GetName();
                TestContext.WriteLine($"Fragment name: {fragmentName}");
            }
        }

        spDestroyCompileRequest(request);

        SlangCompileRequest requestVertex = spCreateCompileRequest(session);

        spSetCodeGenTarget(requestVertex, SlangCompileTarget.SLANG_SPIRV);
        translationUnitIndex = spAddTranslationUnit(requestVertex, SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG, "test_vertex.slang");
        spAddTranslationUnitSourceString(requestVertex, translationUnitIndex, path, code);
        //compile vertex to spirv
        spAddEntryPoint(requestVertex, translationUnitIndex, vertexName, SlangStage.SLANG_STAGE_VERTEX);

        result = spCompile(requestVertex);
        if (result.IsError)
        {
            TestContext.WriteLine(request.GetDiagnosticString());
            Assert.Fail("Failed to compile vertex");
        }

        byte[] vertexSpirv = requestVertex.GetBytes();
        ShaderReflectionInfo vertexReflection = UtilsShaderRelfection.GetSpirvReflection(vertexSpirv);
        TestContext.WriteLine($"Vertex reflection:\n{vertexReflection}");
        //Todo: check reflection
        spDestroyCompileRequest(requestVertex);

        SlangCompileRequest requestFragment = spCreateCompileRequest(session);

        spSetCodeGenTarget(requestFragment, SlangCompileTarget.SLANG_SPIRV);
        translationUnitIndex = spAddTranslationUnit(requestFragment, SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG, "test_vertex.slang");
        spAddTranslationUnitSourceString(requestFragment, translationUnitIndex, path, code);

        //compile fragment to spirv
        spAddEntryPoint(requestFragment, translationUnitIndex, fragmentName, SlangStage.SLANG_STAGE_FRAGMENT);

        result = spCompile(requestFragment);
        if (result.IsError)
        {
            TestContext.WriteLine(request.GetDiagnosticString());
            Assert.Fail("Failed to compile fragment");
        }

        byte[] fragmentSpirv = requestFragment.GetBytes();
        ShaderReflectionInfo fragmentReflection = UtilsShaderRelfection.GetSpirvReflection(fragmentSpirv);
        TestContext.WriteLine($"Fragment reflection:\n{fragmentReflection}");

    }
}