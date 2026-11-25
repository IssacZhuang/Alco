#language slang 2025
module AlcoGUI_Mesh;

import Core;
import SharedSampler;

static const uint UIQuadPremultiplied = 1u;
static const uint UIQuadDistanceGlyph = 2u;
static const uint UIQuadBitmapGlyph = 3u;
static const uint UIQuadStraightTiled = 4u;
static const uint UIQuadPremultipliedTiled = 5u;
static const uint UIQuadStraightNearest = 6u;
static const uint UIQuadPremultipliedNearest = 7u;

struct UIMeshVertex
{
    float2 Position;
    float2 Uv;
    float4 Color;
};

[GPURoot]
public struct UIMeshRoot
{
    public GPUBufferView Vertices;
    public float2 ClipScale;
    public uint TextureIndex;
    public uint Kind;
    public float SdfUnitRange;
    public uint _Pad0;
};

struct UIMeshVsOut
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
    float4 Color : COLOR0;
    nointerpolation uint TextureIndex : TEXCOORD1;
    nointerpolation uint Kind : TEXCOORD2;
};

[shader("vertex")]
UIMeshVsOut vsMain(uint vertexId : SV_VertexID)
{
    UIMeshRoot root = gpu_load_root<UIMeshRoot>();
    UIMeshVertex v = gpu_load<UIMeshVertex>(root.Vertices, vertexId);

    UIMeshVsOut output;
    output.Position = float4(v.Position * root.ClipScale, 0.0, 1.0);
    output.Uv = v.Uv;
    output.Color = v.Color;
    output.TextureIndex = root.TextureIndex;
    output.Kind = root.Kind;
    return output;
}

float4 sampleMeshTexture(uint textureIndex, uint kind, float2 uv)
{
    Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(textureIndex));
    if (kind == UIQuadStraightTiled || kind == UIQuadPremultipliedTiled)
        return tex.SampleLevel(shared_sampler_linear_repeat(), uv, 0.0);
    if (kind == UIQuadStraightNearest || kind == UIQuadPremultipliedNearest)
        return tex.SampleLevel(shared_sampler_nearest_clamp(), uv, 0.0);
    return tex.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0);
}

float evaluateUiSdfCoverage(
    Texture2D<float4> tex,
    float2 uv,
    float2 duvDx,
    float2 duvDy,
    float sdfUnitRange,
    float3 colorRgb)
{
    float uvPerPx = sqrt(0.5 * (dot(duvDx, duvDx) + dot(duvDy, duvDy)));
    float unitRange = sdfUnitRange > 0.0 ? sdfUnitRange : (12.0 / 1024.0);
    float screenPxRange = max(unitRange / max(uvPerPx, 1e-6), 1.0);
    float tapScale = screenPxRange * 2.0;

    float2 d1 = 0.125 * duvDx + 0.375 * duvDy;
    float2 d2 = 0.375 * duvDx - 0.125 * duvDy;

    float s0 = tex.SampleLevel(shared_sampler_linear_clamp(), uv - d1, 0.0).r;
    float s1 = tex.SampleLevel(shared_sampler_linear_clamp(), uv + d2, 0.0).r;
    float s2 = tex.SampleLevel(shared_sampler_linear_clamp(), uv + d1, 0.0).r;
    float s3 = tex.SampleLevel(shared_sampler_linear_clamp(), uv - d2, 0.0).r;

    float c0 = saturate((s0 - 0.5) * tapScale + 0.5);
    float c1 = saturate((s1 - 0.5) * tapScale + 0.5);
    float c2 = saturate((s2 - 0.5) * tapScale + 0.5);
    float c3 = saturate((s3 - 0.5) * tapScale + 0.5);
    float cov = 0.25 * (c0 + c1 + c2 + c3);

    float luma = dot(colorRgb, float3(0.299, 0.587, 0.114));
    float gammaExp = lerp(1.0, 0.72, saturate(luma));
    return cov > 0.0 ? pow(max(cov, 1e-6), gammaExp) : 0.0;
}

[shader("pixel")]
float4 psMain(UIMeshVsOut input) : SV_Target0
{
    // Compute UV derivatives before any control flow on Kind so quad helper lanes stay valid.
    float2 duvDx = ddx(input.Uv);
    float2 duvDy = ddy(input.Uv);

    if (input.Kind == UIQuadDistanceGlyph)
    {
        UIMeshRoot root = gpu_load_root<UIMeshRoot>();
        Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(input.TextureIndex));
        float cov = evaluateUiSdfCoverage(tex, input.Uv, duvDx, duvDy, root.SdfUnitRange, input.Color.rgb);
        float a = input.Color.a * cov;
        return float4(input.Color.rgb * a, a);
    }

    float4 texel = sampleMeshTexture(input.TextureIndex, input.Kind, input.Uv);
    if (input.Kind == UIQuadBitmapGlyph)
    {
        float a = input.Color.a * texel.r;
        return float4(input.Color.rgb * a, a);
    }

    if (input.Kind == UIQuadPremultiplied ||
        input.Kind == UIQuadPremultipliedTiled ||
        input.Kind == UIQuadPremultipliedNearest)
    {
        return float4(texel.rgb * (input.Color.rgb * input.Color.a), texel.a * input.Color.a);
    }

    float4 combined = texel * input.Color;
    return float4(combined.rgb * combined.a, combined.a);
}
