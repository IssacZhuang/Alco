#define READ_ONLY [[vk::ext_decorate(24)]] // OpDecorate NonWritable in SPIR-V
#define WRITE_ONLY [[vk::ext_decorate(25)]] // OpDecorate NonReadable in SPIR-V
#define PUSH_CONSTANT [[vk::push_constant]] // layout(push_constant) in GLSL
#define SLOT(set, bind) [[vk::binding(bind, set)]] // layout(binding = bind, set = set) in GLSL

SLOT(0,0) RWStructuredBuffer<float4> positions;
SLOT(1,0) cbuffer Time{
    float time;
};

[numthreads(8, 1, 1)]
void MainCS(uint3 id: SV_DispatchThreadID) {
    positions[id.x] = float4(id.x, cos(time + float(id.x)*0.1)*5, 0.0f, 1.0f);
}