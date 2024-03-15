using NUnit.Framework;
using Vocore.Graphics;
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
        Assert.That(result.Stages.IsGraphicsShader(), Is.True);
        Assert.That(result.Stages.IsComputeShader(), Is.False);
        Assert.That(result.EntryVertex, Is.EqualTo("vs_main"));
        Assert.That(result.EntryFragment, Is.EqualTo("fs_main"));

        shaderText = @"
        // correct
        #pragma EntryCompute cs_main";
        result = ShaderCompiler.PreprocessText(shaderText, "test.hlsl");
        Assert.DoesNotThrow(() => ShaderCompiler.ValidatePreprocessResult(result));
        Assert.That(result.Stages.IsGraphicsShader(), Is.False);
        Assert.That(result.Stages.IsComputeShader(), Is.True);
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

    [Test(Description = "Shader compilation")]
    public void TestShaderCompilation()
    {
        string shaderText = @"
        #pragma EntryVertex vs_main
        #pragma EntryFragment fs_main

        struct Vertex{
            float3 position : POSITION;
            float4 color : COLOR;
        };


        struct PixelInput{
            float4 position : SV_POSITION;
            float4 color : COLOR;
        };

        PixelInput vs_main(Vertex input){
            PixelInput output;
            output.position = float4(input.position, 1.0f);
            output.color = input.color;
            return output;
        }

        float4 fs_main(PixelInput input) : SV_TARGET{
            return input.color;
        }

        ";

        Assert.DoesNotThrow(() => ShaderCompiler.Compile(shaderText, "test.hlsl"));
    }

    [Test(Description = "Test Serialization")]
    public void TestShaderSerialization()
    {
        string shaderText = @"
[[vk::binding(0,0)]] cbuffer Constants{
    float3 color;
};

[[vk::binding(0,1)]] Texture2D image;
[[vk::binding(1,1)]] SamplerState imageSampler;

#pragma EntryVertex vs_main
#pragma EntryFragment fs_main

#pragma BlendState NonPremultipliedAlpha

struct VertexInput {
    uint instanceID : SV_InstanceID;
    float3 position : POSITION0;
    float3 color : COLOR0;
    float2 texCoord : TEXCOORD0;
};

struct VertexOutput {
    float4 clip_position : SV_POSITION;
    float2 texCoord : TEXCOORD0;
};

VertexOutput vs_main(VertexInput model) {
    VertexOutput v2f;
    v2f.clip_position = float4(model.position, 1);
    v2f.texCoord = model.texCoord;
    return v2f;
}

float4 fs_main(VertexOutput input) : SV_Target0 {
    float4 result =  image.Sample(imageSampler, input.texCoord);
    // inverse gamma correction
    result = pow(result, float4(2.2, 2.2, 2.2, 2.2));
    return result;
}

        ";
        ShaderCompileResult result = ShaderCompiler.Compile(shaderText, "test.hlsl");
        byte[] data = UtilsShaderSerialization.EncodeCompileResult(result);
        ShaderCompileResult result2 = UtilsShaderSerialization.DecodeCompileResult(data);
        Assert.IsTrue(result2.IsGraphicsShader == result.IsGraphicsShader);
        Assert.IsTrue(result2.IsComputeShader == result.IsComputeShader);
        Assert.IsTrue(result2.ReflectionInfo.BindGroups.Length == result.ReflectionInfo.BindGroups.Length);
        Assert.IsTrue(result2.ReflectionInfo.VertexLayouts.Length == result.ReflectionInfo.VertexLayouts.Length);
        Assert.IsTrue(result2.ReflectionInfo.PushConstantsRanges.Length == result.ReflectionInfo.PushConstantsRanges.Length);
        Assert.IsTrue(result2.PreproccessResult.Filename == result.PreproccessResult.Filename);
    }
}
