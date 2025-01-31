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
#define DEFINE_TEX2D_STORAGE(index, name, format) SLOT(index, 0) IMAGE_FORMAT(format) RWTexture2D<float4> name

#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))