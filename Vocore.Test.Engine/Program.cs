using System;
using Vocore;
using Vocore.Engine;
using Veldrid;

using System.Runtime.InteropServices;
using Vocore.ShaderConductor;

// See https://aka.ms/new-console-template for more information
// App app = new App(GraphicsBackend.OpenGL, "Test");
// app.Run();

// using(Game game = new Game())
// {
//     game.RegisterPlugin<PluginBuiltInShader>();
//     game.RegisterPlugin<PluginRuntimeInfo>();
//     game.Run();
// }


string text = @"
cbuffer Matrices : register(b0)
{
    float4x4 worldViewProj;
};

Texture2D DiffuseTexture : register(t0);
SamplerState Sampler : register(s0);

struct VS_IN
{
    float4 pos : POSITION;
    float4 col : COLOR;
    float2 tex : TEXCOORD;
};

struct PS_IN
{
    float4 pos : SV_POSITION;
    float4 col : COLOR;
    float2 tex : TEXCOORD;
};

PS_IN VS(VS_IN input)
{
    PS_IN output = (PS_IN)0;

    output.pos = mul(input.pos, worldViewProj);
    output.col = input.col;
    output.tex = input.tex;

    return output;
}

float4 PS(PS_IN input) : SV_Target
{
    return DiffuseTexture.Sample(Sampler, input.tex);
}
";


ShaderConductor.SourceDesc sourceDesc = new ShaderConductor.SourceDesc
{
source = text,
entryPoint = "VS",
stage = ShaderConductor.ShaderStage.VertexShader,
};

ShaderConductor.OptionsDesc optionsDesc = ShaderConductor.OptionsDesc.Default;

ShaderConductor.TargetDesc targetDesc = new ShaderConductor.TargetDesc
{
    language = ShaderConductor.ShadingLanguage.Hlsl,
    version = null,
};

ShaderConductor.Compile(ref sourceDesc, ref optionsDesc, ref targetDesc, out ShaderConductor.ResultDesc resultDesc);
Log.Info(resultDesc.hasError);
Log.Info(Marshal.PtrToStringAnsi(ShaderConductor.GetShaderConductorBlobData(resultDesc.target)));
foreach (var item in ShaderConductor.GetUniformBuffers(resultDesc))
{
    Log.Info(item.blockName, item.instanceName);
}

