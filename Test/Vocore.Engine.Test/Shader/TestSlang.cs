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

        spDestroyCompileRequest(request);
        spDestroySession(session);

        Assert.That(entryCount, Is.EqualTo(2));


    }

    [Test(Description = "Test slang spirv gen")]
    public void TestSpirvGen()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Slang", "shader.slang");
        var code = File.ReadAllText(path);

        SlangSession session = spCreateSession("test_session");
        SlangCompileRequest request = spCreateCompileRequest(session);

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

        spDestroyCompileRequest(request);
        spDestroySession(session);
    }

    [Test(Description = "Test slang add library reference")]
    public void TestLibraryReference()
    {
        SlangSession session = spCreateSession("test_session");

        var pathLibrary = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Slang", "library/lib.slang");
        var codeLibrary = File.ReadAllText(pathLibrary);

        SlangCompileRequest requestLibrary = spCreateCompileRequest(session);
        spSetCodeGenTarget(requestLibrary, SlangCompileTarget.SLANG_SHADER_SHARED_LIBRARY);
        int translationUnitIndexLibrary = spAddTranslationUnit(requestLibrary, SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG, "lib.slang");
        spAddTranslationUnitSourceString(requestLibrary, translationUnitIndexLibrary, "lib.slang", codeLibrary);
        SlangResult resultLibrary = spCompile(requestLibrary);

        if (resultLibrary.IsError)
        {
            Assert.Fail("Failed to compile library");
        }

        //error: pointer zero
        IntPtr ptrLibrary = spGetCompileRequestCode(requestLibrary, out nuint sizeLibrary);

        TestContext.WriteLine(ptrLibrary);


        var pathShader = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Slang", "shader.slang");
        var codeShader = File.ReadAllText(pathShader);


        SlangCompileRequest requestShader = spCreateCompileRequest(session);

        spSetCodeGenTarget(requestShader, SlangCompileTarget.SLANG_SPIRV);

        //add library reference manually
        

        int translationUnitIndex = spAddTranslationUnit(requestShader, SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG, "test_vertex.slang");
        //use a fake path to avoid comppiler find the library by it path
        spAddTranslationUnitSourceString(requestShader, translationUnitIndex, "test.slang", codeShader);
        
        SlangResult resultAddLib = spAddLibraryReference(requestShader, "library/lib.slang", ptrLibrary, sizeLibrary);

        if (resultAddLib.IsError)
        {
            Assert.Fail("Failed to add library reference");
        }

        SlangResult result = spCompile(requestShader);

        TestContext.WriteLine(requestShader.GetDiagnosticString());
        if (result.IsError)
        {
            Assert.Fail("Failed to compile");
        }

        SlangReflection reflection = spGetReflection(requestShader);

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

        spDestroySession(session);

        Assert.That(vertexIndex, Is.EqualTo(0));
        Assert.That(fragmentIndex, Is.EqualTo(1));


    }
}