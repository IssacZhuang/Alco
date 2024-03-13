using NUnit.Framework;
using Vocore.Rendering;

namespace Vocore.Engine.Test;

public class TestShader
{
    [Test(Description ="Shader pragmas")]
    public void TestShaderPragmas()
    {
        string shaderText = @"
                // Some comments
                #pragma pragma1 value1 value2
                #pragma pragma2 value3 value4
                // More comments
                #pragma pragma3 value5
                // Commented pragma, should not be parsed
                //#pragma pragma4 value6

                /* comment block
                #pragma pragma5 value7
                */
                ";

        // Act
        ShaderPragma[] pragmas = ShaderCompiler.PreprocessText(shaderText, "test.hlsl").Pragmas;

        // Assert
        Assert.That(pragmas.Length, Is.EqualTo(3));

        Assert.That(pragmas[0].Name, Is.EqualTo("pragma1"));
        Assert.That(pragmas[0].Values.Length, Is.EqualTo(2));
        Assert.That(pragmas[0].Values[0], Is.EqualTo("value1"));
        Assert.That(pragmas[0].Values[1], Is.EqualTo("value2"));

        Assert.That(pragmas[1].Name, Is.EqualTo("pragma2"));
        Assert.That(pragmas[1].Values.Length, Is.EqualTo(2));
        Assert.That(pragmas[1].Values[0], Is.EqualTo("value3"));
        Assert.That(pragmas[1].Values[1], Is.EqualTo("value4"));

        Assert.That(pragmas[2].Name, Is.EqualTo("pragma3"));
        Assert.That(pragmas[2].Values.Length, Is.EqualTo(1));
        Assert.That(pragmas[2].Values[0], Is.EqualTo("value5"));
    }

    [Test(Description = "Shader no pragmas")]
    public void TestShaderNoPragmas(){
        string shaderText = @"
                // Some comments
                // More comments";

        // Act
        ShaderPragma[] pragmas = ShaderCompiler.PreprocessText(shaderText, "test.hlsl").Pragmas;

        // Assert
        Assert.That(pragmas.Length, Is.EqualTo(0));
    }

    [Test(Description = "Shader validate entries")]
    public void TestValidationEntries()
    {
        string shaderText = @"";
        ShaderPreproccessResult result = ShaderCompiler.PreprocessText(shaderText, "test.hlsl");
        Assert.Catch<ShaderValidationException>(() => ShaderCompiler.ValidatePreprocessResult(result));

        shaderText = @"
        // no fragment entry
        #pragma EntryVertex vs_main
        ";
        result = ShaderCompiler.PreprocessText(shaderText, "test.hlsl");
        Assert.Catch<ShaderValidationException>(() => ShaderCompiler.ValidatePreprocessResult(result));

        shaderText = @"
        // no vertex entry
        #pragma EntryFragment fs_main
        ";
        result = ShaderCompiler.PreprocessText(shaderText, "test.hlsl");
        Assert.Catch<ShaderValidationException>(() => ShaderCompiler.ValidatePreprocessResult(result));

        shaderText = @"
        // correct
        #pragma EntryVertex vs_main
        #pragma EntryFragment fs_main";
        result = ShaderCompiler.PreprocessText(shaderText, "test.hlsl");
        Assert.DoesNotThrow(() => ShaderCompiler.ValidatePreprocessResult(result));
        Assert.That(result.IsGraphicsShader, Is.True);
        Assert.That(result.IsComputeShader, Is.False);
        Assert.That(result.EntryVertex, Is.EqualTo("vs_main"));
        Assert.That(result.EntryFragment, Is.EqualTo("fs_main"));

        shaderText = @"
        // correct
        #pragma EntryCompute cs_main";
        result = ShaderCompiler.PreprocessText(shaderText, "test.hlsl");
        Assert.DoesNotThrow(() => ShaderCompiler.ValidatePreprocessResult(result));
        Assert.That(result.IsGraphicsShader, Is.False);
        Assert.That(result.IsComputeShader, Is.True);
        Assert.That(result.EntryCompute, Is.EqualTo("cs_main"));

        shaderText = @"
        // conflict
        #pragma EntryVertex vs_main
        #pragma EntryCompute cs_main
        #pragma EntryFragment fs_main
        ";
        result = ShaderCompiler.PreprocessText(shaderText, "test.hlsl");
        Assert.Catch<ShaderValidationException>(() => ShaderCompiler.ValidatePreprocessResult(result));
    }

}
