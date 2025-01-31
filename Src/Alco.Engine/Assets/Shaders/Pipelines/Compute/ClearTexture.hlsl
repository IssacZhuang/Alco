#include "Shaders/Libs/Core.hlsli"

struct Constants{
    float4 color;
};

DEFINE_TEX2D_STORAGE(0, _texture, "rgba16f");
PUSH_CONSTANT Constants constants;

[shader("compute")]
[numthreads(16, 16, 1)]
void MainCS(uint3 dispatchThreadId : SV_DispatchThreadID) {
    _texture[dispatchThreadId.xy] = constants.color;
}
