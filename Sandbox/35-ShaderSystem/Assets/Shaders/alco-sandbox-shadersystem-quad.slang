// file "alco-sandbox-shadersystem-quad.slang" - module "alco-sandbox-shadersystem-quad"
//
// Sandbox 35: a shader consumed as a native slang MODULE through ShaderSystem
// (plan Phase 1) instead of an HLSL asset. It imports the ported core module
// (Alco.Rendering.Core) — editing either file and saving triggers module
// invalidation and a hot rebuild of the pipeline.

#language slang 2025
module alco_sandbox_shadersystem_quad;

import alco_rendering_core;

public struct FrameParams
{
    public float4x4 viewProjection;
    public float4 tintColor;
};

public ParameterBlock<FrameParams> _frame;

// Engine convention: one binding index per resource within a set (WebGPU
// layouts reject a texture and sampler sharing an index). Sampling goes
// through the shared sampler bank of the core module — a custom sampler
// member would need a material-bound override (ShaderParameterSet.SetSampler).
// The struct avoids the name MaterialParams: the core module declares an
// attribute of that name and an import makes the reference ambiguous.
public struct MaterialParamsData
{
    public Texture2D _albedo;
};

public ParameterBlock<MaterialParamsData> _material;

struct Vertex
{
    float3 position : POSITION;
    float2 uv       : TEXCOORD0;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv       : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output;
    output.position = mul(_frame.viewProjection, float4(input.position, 1.0));
    output.uv = input.uv;
    return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET
{
    // The core import is load-bearing: SampleTex2D and OutputDither8Bit live
    // in Alco.Rendering.Core, so editing that module invalidates this shader.
    float4 color = SampleTex2D(_material._albedo, _samplers._linearRepeat, input.uv) * _frame.tintColor;
    color.rgb += OutputDither8Bit(input.position.xy);
    return color;
}
