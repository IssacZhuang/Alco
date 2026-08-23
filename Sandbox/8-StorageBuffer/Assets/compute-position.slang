// file "compute-position.slang" - module "sandbox8_compute_position"
//
// Slang module converted from ComputePosition.hlsl (Phase 4 migration): the
// low-level sandbox compiles it through the native slang module API. The
// READ_ONLY/WRITE_ONLY extension macros became native declarations — slang
// decorates storage buffers from their usage.

#language slang 2025
module sandbox8_compute_position;

[[vk::binding(0, 0)]] RWStructuredBuffer<float4> positions;
[[vk::binding(0, 1)]] cbuffer Time{
    float time;
};

[shader("compute")]
[numthreads(8, 1, 1)]
void MainCS(uint3 id: SV_DispatchThreadID) {
    positions[id.x] = float4(id.x, cos(time + float(id.x)*0.1)*5, 0.0f, 1.0f);
}
