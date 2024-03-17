#define PI 3.141592
#define TAU 6.283185
#define E 2.718281

#define READ_ONLY [[vk::ext_decorate(24)]] // OpDecorate NonWritable in SPIR-V
#define WRITE_ONLY [[vk::ext_decorate(25)]] // OpDecorate NonReadable in SPIR-V
#define PUSH_CONSTANT [[vk::push_constant]] // layout(push_constant) in GLSL
#define SLOT(set, bind) [[vk::binding(bind, set)]] // layout(binding = bind, set = set) in GLSL

#define VAR_STRUCT(index, name) SLOT(index, 0) cbuffer name
#define VAR_TEXTURE_2D(index, name) SLOT(index, 0) Texture2D name; SamplerState name##Sampler
#define VAR_WRITE_TEXTURE_2D(index, name) SLOT(index, 0) WRITE_ONLY Texture2D name
#define VAR_READ_TEXTURE_2D(index, name) SLOT(index, 0) READ_ONLY Texture2D name

#define SAMPLE_TEXTURE_2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define GET_PIXEL_TEXTURE_2D(textureName, position) textureName.Load(int3(position, 0))