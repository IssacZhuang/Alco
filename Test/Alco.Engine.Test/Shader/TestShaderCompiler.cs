using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;
using Alco.ShaderCompiler;

namespace Alco.Engine.Test;

public class TestShaderCompiler
{
    [Test(Description = "Shader compiler find entries")]
    public void TestShaderCompilerFindEntries()
    {
        string shaderText = @"struct Vertex{
    float3 position : POSITION;
    float4 color : COLOR;
};


struct PixelInput{
    float4 position : SV_POSITION;
    float4 color : COLOR;
};

[shader(""vertex"")]
PixelInput MainVS(Vertex input) {
    PixelInput output;
    output.position = float4(input.position, 1.0f);
    output.color = input.color;
    return output;
}

[shader(""fragment"")]
float4 MainPS(PixelInput input) : SV_TARGET {
    return input.color;
}
";

        var funtions = UtilsShaderHLSL.GetFunctionInfo(shaderText);
        
        Assert.That(funtions.Count, Is.EqualTo(2));
        Assert.That(funtions[0].Name, Is.EqualTo("MainVS"));
        Assert.That(funtions[0].Stage, Is.EqualTo(ShaderStage.Vertex));

        Assert.That(funtions[1].Name, Is.EqualTo("MainPS"));
        Assert.That(funtions[1].Stage, Is.EqualTo(ShaderStage.Fragment));


        shaderText = @"struct Vertex{
    float3 position : POSITION;
    float4 color : COLOR;
};


struct PixelInput{
    float4 position : SV_POSITION;
    float4 color : COLOR;
};

[shader(""vertex"")]
PixelInput MainVS(Vertex input) {
    PixelInput output;
    output.position = float4(input.position, 1.0f);
    output.color = input.color;
    return output;
}

[shader(""pixel"")]
float4 MainPS(PixelInput input) : SV_TARGET {
    return input.color;
}
";

        funtions = UtilsShaderHLSL.GetFunctionInfo(shaderText);
        
        Assert.That(funtions.Count, Is.EqualTo(2));
        Assert.That(funtions[0].Name, Is.EqualTo("MainVS"));
        Assert.That(funtions[0].Stage, Is.EqualTo(ShaderStage.Vertex));

        Assert.That(funtions[1].Name, Is.EqualTo("MainPS"));
        Assert.That(funtions[1].Stage, Is.EqualTo(ShaderStage.Fragment));

        shaderText = @"
        //dxcompiler macro
#define writeonly [[vk::ext_decorate(25)]]
#define rgba8 [[vk::image_format(""rgba8"")]]


Texture2D inputTexture : register(t0, space0);
        writeonly rgba8 RWTexture2D<float4> outputTexture: register(u0, space1);

        cbuffer Constants : register(b0, space2) { int iterations; };

        [numthreads(8, 8, 1)]
        [shader(""compute"")]
        void MainCS(uint3 id: SV_DispatchThreadID) {
            // box blur
            float4 color = inputTexture.Load(id.xyz);

            int size = iterations;
            for (int i = -size; i <= size; i++)
            {
                for (int j = -size; j <= size; j++)
                {
                    color = color + inputTexture.Load(id.xyz + int3(i, j, 0));
                }
            }

            color /= (2 * size + 1) * (2 * size + 1);
            outputTexture[id.xy] = color;
        }

        ";

        funtions = UtilsShaderHLSL.GetFunctionInfo(shaderText);
        
        Assert.That(funtions.Count, Is.EqualTo(1));
        Assert.That(funtions[0].Name, Is.EqualTo("MainCS"));
        Assert.That(funtions[0].Attributes.Count, Is.EqualTo(2));
        Assert.That(funtions[0].Stage, Is.EqualTo(ShaderStage.Compute));

        shaderText = @"
        //dxcompiler macro
#define writeonly [[vk::ext_decorate(25)]]
#define rgba8 [[vk::image_format(""rgba8"")]]


Texture2D inputTexture : register(t0, space0);
        writeonly rgba8 RWTexture2D<float4> outputTexture: register(u0, space1);

        cbuffer Constants : register(b0, space2) { int iterations; };

        [shader(""compute"")]
        [numthreads(8, 8, 1)]
        void MainCS(uint3 id: SV_DispatchThreadID) {
            // box blur
            float4 color = inputTexture.Load(id.xyz);

            int size = iterations;
            for (int i = -size; i <= size; i++)
            {
                for (int j = -size; j <= size; j++)
                {
                    color = color + inputTexture.Load(id.xyz + int3(i, j, 0));
                }
            }

            color /= (2 * size + 1) * (2 * size + 1);
            outputTexture[id.xy] = color;
        }

        ";

        funtions = UtilsShaderHLSL.GetFunctionInfo(shaderText);

        Assert.That(funtions.Count, Is.EqualTo(1));
        Assert.That(funtions[0].Name, Is.EqualTo("MainCS"));
        Assert.That(funtions[0].Attributes.Count, Is.EqualTo(2));
        Assert.That(funtions[0].Stage, Is.EqualTo(ShaderStage.Compute));

    }

    


}
