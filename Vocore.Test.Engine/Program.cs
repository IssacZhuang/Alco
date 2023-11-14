using System;
using Vocore;
using Vocore.Engine;
using Veldrid;

using System.Runtime.InteropServices;
using Vocore.ShaderCross;
using Veldrid.SPIRV;

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

Texture2D DiffuseTexture;
SamplerState Sampler;

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


VertexFragmentCompilationResult result = HlslCross.ComplieShader(text, CrossCompileTarget.HLSL, "VS", "PS");

Log.Info(result.VertexShader);
Log.Info(result.FragmentShader);

foreach (var item in result.Reflection.VertexElements)
{
    Log.Info(item.ToString());
}

foreach (var item in result.Reflection.ResourceLayouts)
{
    Log.Info(item.ToString());
}
