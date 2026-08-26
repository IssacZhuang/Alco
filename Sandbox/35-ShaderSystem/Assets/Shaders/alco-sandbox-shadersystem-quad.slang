// file "alco-sandbox-shadersystem-quad.slang" - module "alco-sandbox-shadersystem-quad"
//
// Sandbox 35: a shader consumed as a native slang MODULE through ShaderSystem.
// Imports the core module (Alco.Rendering.Core) — editing either file and
// saving triggers module invalidation and a hot rebuild of the pipeline.

#language slang 2025
module alco_sandbox_shadersystem_quad;

import alco_rendering_core;

public struct FrameParams
{
    public float4x4 viewProjection;
    public float4 tintColor;
};

public ParameterBlock<FrameParams> frame;

// Engine convention: one binding index per resource within a set (WebGPU
// layouts reject a texture and sampler sharing an index). Sampling goes
// through the shared sampler bank of the core module — a custom sampler
// member would need a material-bound override (ShaderParameterSet.SetSampler).
// The struct avoids the name MaterialParams: the core module declares an
// attribute of that name and an import makes the reference ambiguous.
public struct MaterialParamsData
{
    public Texture2D albedo;
};

public ParameterBlock<MaterialParamsData> material;

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
    output.position = mul(frame.viewProjection, float4(input.position, 1.0));
    output.uv = input.uv;
    return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET
{
    // The core import is load-bearing: sampleTex2D and outputDither8Bit live
    // in Alco.Rendering.Core, so editing that module invalidates this shader.
    float4 color = sampleTex2D(material.albedo, samplers.linearRepeat, input.uv) * frame.tintColor;
    color.rgb += outputDither8Bit(input.position.xy);
    return color;
}
