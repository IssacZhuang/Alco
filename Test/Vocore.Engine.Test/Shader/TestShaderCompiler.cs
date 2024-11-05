using NUnit.Framework;
using Vocore.Graphics;
using Vocore.Rendering;
using Vocore.ShaderCompiler;

namespace Vocore.Engine.Test;

public class TestShaderCompiler
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
        ShaderPragma[] pragmas = UtilsShaderHLSL.PreprocessText(shaderText, "test.hlsl").Pragmas;

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
        ShaderPragma[] pragmas = UtilsShaderHLSL.PreprocessText(shaderText, "test.hlsl").Pragmas;

        // Assert
        Assert.That(pragmas.Length, Is.EqualTo(0));
    }

    [Test(Description = "Shader validate entries")]
    public void TestValidationEntries()
    {
        string shaderText = @"";
        ShaderPreproccessResultHLSLDeprecated result = UtilsShaderHLSL.PreprocessText(shaderText, "test.hlsl");
        Assert.Catch<ShaderValidationException>(() => UtilsShaderHLSL.ValidatePreprocessResult(result));

        shaderText = @"
        // no fragment entry
        #pragma EntryVertex MainVS
        ";
        result = UtilsShaderHLSL.PreprocessText(shaderText, "test.hlsl");
        Assert.Catch<ShaderValidationException>(() => UtilsShaderHLSL.ValidatePreprocessResult(result));

        shaderText = @"
        // no vertex entry
        #pragma EntryFragment MainPS
        ";
        result = UtilsShaderHLSL.PreprocessText(shaderText, "test.hlsl");
        Assert.Catch<ShaderValidationException>(() => UtilsShaderHLSL.ValidatePreprocessResult(result));

        shaderText = @"
        // correct
        #pragma EntryVertex MainVS
        #pragma EntryFragment MainPS";
        result = UtilsShaderHLSL.PreprocessText(shaderText, "test.hlsl");
        Assert.DoesNotThrow(() => UtilsShaderHLSL.ValidatePreprocessResult(result));
        Assert.That(result.Stages.IsGraphicsShader(), Is.True);
        Assert.That(result.Stages.IsComputeShader(), Is.False);
        Assert.That(result.EntryVertex, Is.EqualTo("MainVS"));
        Assert.That(result.EntryFragment, Is.EqualTo("MainPS"));

        shaderText = @"
        // correct
        #pragma EntryCompute MainCS";
        result = UtilsShaderHLSL.PreprocessText(shaderText, "test.hlsl");
        Assert.DoesNotThrow(() => UtilsShaderHLSL.ValidatePreprocessResult(result));
        Assert.That(result.Stages.IsGraphicsShader(), Is.False);
        Assert.That(result.Stages.IsComputeShader(), Is.True);
        Assert.That(result.EntryCompute, Is.EqualTo("MainCS"));

        shaderText = @"
        // conflict
        #pragma EntryVertex MainVS
        #pragma EntryCompute MainCS
        #pragma EntryFragment MainPS
        ";
        result = UtilsShaderHLSL.PreprocessText(shaderText, "test.hlsl");
        Assert.Catch<ShaderValidationException>(() => UtilsShaderHLSL.ValidatePreprocessResult(result));
    }

    [Test(Description = "Shader compilation")]
    public void TestShaderCompilation()
    {
        string shaderText = @"
        #pragma EntryVertex MainVS
        #pragma EntryFragment MainPS

        struct Vertex{
            float3 position : POSITION;
            float4 color : COLOR;
        };


        struct PixelInput{
            float4 position : SV_POSITION;
            float4 color : COLOR;
        };

        PixelInput MainVS(Vertex input){
            PixelInput output;
            output.position = float4(input.position, 1.0f);
            output.color = input.color;
            return output;
        }

        float4 MainPS(PixelInput input) : SV_TARGET{
            return input.color;
        }

        ";

        Assert.DoesNotThrow(() => UtilsShaderHLSL.Compile(shaderText, "test.hlsl"));
    }
}
