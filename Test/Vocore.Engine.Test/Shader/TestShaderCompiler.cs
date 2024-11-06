using NUnit.Framework;
using Vocore.Graphics;
using Vocore.Rendering;
using Vocore.ShaderCompiler;

namespace Vocore.Engine.Test;

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
        TestContext.WriteLine(funtions.Count);
        foreach (var fun in funtions)
        {
            TestContext.WriteLine(fun);
        }

        Assert.IsTrue(UtilsShaderHLSL.TryFindEntryVertex(shaderText, out string vertex));
        Assert.That(vertex, Is.EqualTo("MainVS"));

        Assert.IsTrue(UtilsShaderHLSL.TryFindEntryFragment(shaderText, out string fragment));
        Assert.That(fragment, Is.EqualTo("MainPS"));

        Assert.IsFalse(UtilsShaderHLSL.TryFindEntryCompute(shaderText, out string compute));

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

        Assert.IsFalse(UtilsShaderHLSL.TryFindEntryFragment(shaderText, out string pixel));
        Assert.That(pixel, Is.EqualTo("MainPS"));
    }


}
