#include "Shaders/Libs/Core.hlsli"

struct Constants{
    float4 color;
};

DEFINE_TEX2D_STORAGE(0, _texture, float4, "rgba16f");

[shader("compute")]
[numthreads(16, 16, 1)]
void MainCS(uint3 id : SV_DispatchThreadID) {
    _texture[id.xy] = float4(0, 0, 0, 0);
}
