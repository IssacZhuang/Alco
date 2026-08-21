#ifndef CORE_HLSLI
#define CORE_HLSLI

#define PI 3.141592
#define TAU 6.283185
#define EULER 2.718281
#define ALPHA_CLIP 0.05

//simulate the slang keyword
#define vk_push_constant vk::push_constant
#define vk_image_format vk::image_format

#define readonly [[vk::ext_decorate(24)]] // OpDecorate NonWritable in SPIR-V
#define writeonly [[vk::ext_decorate(25)]] // OpDecorate NonReadable in SPIR-V


#define PUSH_CONSTANT [[vk::push_constant]] // layout(push_constant) in GLSL
#define IMAGE_FORMAT(format) [[vk::image_format(format)]]

// Set-only annotation: register(spaceN) without a register number makes DXC assign
// binding numbers automatically, sequentially per set in declaration order (the
// sampler companion of a sampled texture takes the binding right after its
// texture). Two-level indirection is required so the group constant is expanded
// before token pasting.
#define ALCO_PASTE_(a, b) a##b
#define ALCO_PASTE(a, b) ALCO_PASTE_(a, b)
#define ALCO_SET(set) register(ALCO_PASTE(space, set))

// Resource declaration macros. `index` is the bind group (set) index; bindings
// inside a set are assigned automatically by the compiler in declaration order,
// so multiple resources can share one set simply by being declared one after
// another. The engine resolves resources by name, never by binding number.
#define DEFINE_UNIFORM(index, name) cbuffer name : ALCO_SET(index)
#define DEFINE_STORAGE(index, type, name) RWStructuredBuffer<type> name : ALCO_SET(index)
#define DEFINE_TEX2D_SAMPLE(index, name) Texture2D name : ALCO_SET(index); SamplerState name##Sampler : ALCO_SET(index)
#define DEFINE_TEX2D_READ(index, name) Texture2D name : ALCO_SET(index)
#define DEFINE_TEX2D_STORAGE(index, name, type, format) IMAGE_FORMAT(format) RWTexture2D<type> name : ALCO_SET(index)
#define DEFINE_TEX3D_SAMPLE(index, name) Texture3D name : ALCO_SET(index); SamplerState name##Sampler : ALCO_SET(index)
#define DEFINE_TEX3D_READ(index, name) Texture3D name : ALCO_SET(index)
#define DEFINE_TEX3D_STORAGE(index, name, type, format) IMAGE_FORMAT(format) RWTexture3D<type> name : ALCO_SET(index)

// Depth textures. DXC cannot mark a texture as a depth image in SPIR-V, so textures
// declared with these macros are rewritten to depth images after compilation
// (see SpirvDepthTexturePatcher) and must be bound via SetRenderTextureDepth.
// DEFINE_TEX2D_DEPTH: Load-only depth texture (raw depth reads).
// DEFINE_TEX2D_DEPTH_SAMPLE: depth texture + comparison sampler pair (shadow map PCF).
#define DEFINE_TEX2D_DEPTH(index, name) Texture2D<float> name : ALCO_SET(index)
#define DEFINE_TEX2D_DEPTH_SAMPLE(index, name) Texture2D<float> name : ALCO_SET(index); SamplerComparisonState name##Sampler : ALCO_SET(index)

// Triangular-PDF dither based on interleaved gradient noise (Jimenez 2014), scaled to
// +/-1 code of an 8-bit UNORM target. Add it to the final LDR color before the output
// is quantized to the swapchain to hide banding in dark gradients.
float OutputDither8Bit(float2 pixelPos)
{
    const float2 k = float2(0.06711056f, 0.00583715f);
    float r1 = frac(52.9829189f * frac(dot(pixelPos, k)));
    float r2 = frac(52.9829189f * frac(dot(pixelPos + 0.5f, k)));
    return (r1 + r2 - 1.0f) * (1.0f / 255.0f);
}


#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
// Hardware bilinear sampling of a 2D texture at an explicit mip level
// (paired by DEFINE_TEX2D_SAMPLE); usable from compute shaders.
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
// Hardware trilinear sampling of a 3D texture at an explicit mip level (paired by DEFINE_TEX3D_SAMPLE).
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
// Exact texel fetch of a 3D texture at an explicit mip level (paired by DEFINE_TEX3D_READ).
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
// Hardware depth comparison sampling (comparison sampler paired by DEFINE_TEX2D_DEPTH_SAMPLE):
// returns 1.0 when compareDepth <= texel depth (lit), 0.0 otherwise, filtered by the sampler.
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

#endif // CORE_HLSLI
