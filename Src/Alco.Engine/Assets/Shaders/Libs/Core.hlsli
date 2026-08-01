#define PI 3.141592
#define TAU 6.283185
#define EULER 2.718281
#define ALPHA_CLIP 0.05

//simulate the slang keyword
#define vk_binding vk::binding
#define vk_push_constant vk::push_constant
#define vk_image_format vk::image_format

#define readonly [[vk::ext_decorate(24)]] // OpDecorate NonWritable in SPIR-V
#define writeonly [[vk::ext_decorate(25)]] // OpDecorate NonReadable in SPIR-V


#define PUSH_CONSTANT [[vk::push_constant]] // layout(push_constant) in GLSL
#define SLOT(set, bind) [[vk_binding(bind, set)]] // layout(binding = bind, set = set) in GLSL
#define IMAGE_FORMAT(format) [[vk::image_format(format)]]

#define DEFINE_UNIFORM(index, name) SLOT(index, 0) cbuffer name
#define DEFINE_STORAGE(index, type, name) SLOT(index, 0) RWStructuredBuffer<type> name
#define DEFINE_TEX2D_SAMPLE(index, name) SLOT(index, 0) Texture2D name; SLOT(index, 1) SamplerState name##Sampler
#define DEFINE_TEX2D_READ(index, name) SLOT(index, 0) Texture2D name
#define DEFINE_TEX2D_STORAGE(index, name, type, format) SLOT(index, 0) IMAGE_FORMAT(format) RWTexture2D<type> name
#define DEFINE_TEX3D_SAMPLE(index, name) SLOT(index, 0) Texture3D name; SLOT(index, 1) SamplerState name##Sampler
#define DEFINE_TEX3D_READ(index, name) SLOT(index, 0) Texture3D name
#define DEFINE_TEX3D_STORAGE(index, name, type, format) SLOT(index, 0) IMAGE_FORMAT(format) RWTexture3D<type> name

// Depth textures. DXC cannot mark a texture as a depth image in SPIR-V, so textures
// declared with these macros are rewritten to depth images after compilation
// (see SpirvDepthTexturePatcher) and must be bound via SetRenderTextureDepth.
// DEFINE_TEX2D_DEPTH: Load-only depth texture (raw depth reads).
// DEFINE_TEX2D_DEPTH_SAMPLE: depth texture + comparison sampler pair (shadow map PCF).
#define DEFINE_TEX2D_DEPTH(index, name) SLOT(index, 0) Texture2D<float> name
#define DEFINE_TEX2D_DEPTH_SAMPLE(index, name) SLOT(index, 0) Texture2D<float> name; SLOT(index, 1) SamplerComparisonState name##Sampler


#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
// Hardware trilinear sampling of a 3D texture at an explicit mip level (paired by DEFINE_TEX3D_SAMPLE).
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
// Exact texel fetch of a 3D texture at an explicit mip level (paired by DEFINE_TEX3D_READ).
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
// Hardware depth comparison sampling (comparison sampler paired by DEFINE_TEX2D_DEPTH_SAMPLE):
// returns 1.0 when compareDepth <= texel depth (lit), 0.0 otherwise, filtered by the sampler.
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)