// file "alco-rendering-core.slang" - module "alco.rendering.core"
//
// The Slang-native port of Shaders/Libs/Core.hlsli (plan Phase 1.3). The HLSL
// file stays for the DXC path; new modules import this one instead. Everything
// shared across engine shader modules lives here: constants, the frequency-
// group set indices, sampling helpers over real sampler types (replacing the
// SAMPLE_* macro layer) and small math utilities.

#language slang 2025
module alco_rendering_core;

// ── shared constants ────────────────────────────────────────────────────────

public static const float PI = 3.141592;
public static const float TAU = 6.283185;
public static const float EULER = 2.718281;
public static const float ALPHA_CLIP = 0.05;

// ── frequency-group set indices (MaterialBindGroupRefactorPlan.md §3.1) ─────
// Bind groups are organized by update frequency; within a set, binding numbers
// are compiler-assigned in declaration order and never read by the engine
// (name-based resolution, ShaderReflectionInfo).

public static const int ALCO_GROUP_FRAME = 0;    // per-frame globals (camera, time)
public static const int ALCO_GROUP_PASS = 1;     // per-pass resources
public static const int ALCO_GROUP_MATERIAL = 2; // per-material parameters
public static const int ALCO_GROUP_DRAW = 3;     // per-draw instance data

// ── material-parameter blocks ───────────────────────────────────────────────
// The material system's one shader-side convention every pipeline family shares:
// a surface marks its material-parameter cbuffers with [MaterialParams] (any
// block name, any number of blocks, scalar/vector float members may mix) in its
// material set; the engine discovers the marked blocks from Slang reflection
// and packs them from the material asset's parameter table. Unmarked blocks
// (engine data such as _globalRenderData) are never treated as parameters.

[__AttributeUsage(_AttributeTargets.Var)]
public struct MaterialParams {};

// ── sampling helpers (real sampler types; the name##Sampler macro retires) ──

public float4 SampleTex2D(Texture2D textureName, SamplerState samplerState, float2 uv)
{
    return textureName.Sample(samplerState, uv);
}

public float4 SampleTex2DLevel(Texture2D textureName, SamplerState samplerState, float2 uv, float mip)
{
    return textureName.SampleLevel(samplerState, uv, mip);
}

public float4 SampleTex3DLevel(Texture3D textureName, SamplerState samplerState, float3 uvw, float mip)
{
    return textureName.SampleLevel(samplerState, uvw, mip);
}

public float4 GetPixelTex2D(Texture2D textureName, int2 position)
{
    return textureName.Load(int3(position, 0));
}

// Native load from a depth texture. Slang reflects this as a depth-sampled
// texture, so the renderer binds the actual framebuffer depth attachment.
public float GetPixelDepth2D(DepthTexture2D textureName, int2 position)
{
    return textureName.Load(int3(position, 0));
}

public float4 LoadTex3D(Texture3D textureName, int3 coord, int mip)
{
    return textureName.Load(int4(coord, mip));
}

// Hardware depth comparison (shadow map PCF): returns 1.0 when compareDepth is
// behind the stored depth (lit), 0.0 otherwise, filtered by the sampler.
public float SampleTex2DDepthCompare(
    Texture2D<float> textureName, SamplerComparisonState samplerState, float2 uv, float compareDepth)
{
    return textureName.SampleCmpLevelZero(samplerState, uv, compareDepth);
}

// ── math utilities ──────────────────────────────────────────────────────────

// Triangular-PDF dither based on interleaved gradient noise (Jimenez 2014),
// scaled to +/-1 code of an 8-bit UNORM target. Add it to the final LDR color
// before the output is quantized to the swapchain to hide banding in dark
// gradients.
public float OutputDither8Bit(float2 pixelPos)
{
    const float2 k = float2(0.06711056f, 0.00583715f);
    float r1 = frac(52.9829189f * frac(dot(pixelPos, k)));
    float r2 = frac(52.9829189f * frac(dot(pixelPos + 0.5, k)));
    return (r1 + r2 - 1.0) * (1.0 / 255.0);
}
