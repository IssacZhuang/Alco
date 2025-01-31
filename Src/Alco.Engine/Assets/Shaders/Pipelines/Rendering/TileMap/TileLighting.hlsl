#include "Shaders/Libs/Core.hlsli"

//light map texture
DEFINE_TEX2D_READ_WRITE(0, _texture, "rgba16f");

bool IsObstacle(uint2 pos) {
    // todo: light will be blocked by wall
    return false;
}

[shader("compute")]
[numthreads(16, 16, 1)]
void MainCS(uint3 dispatchThreadId: SV_DispatchThreadID) {
    uint2 pos = dispatchThreadId.xy;

    if(IsObstacle(pos)) {
        _texture[dispatchThreadId.xy] = float4(0, 0, 0, 1);
        return;
    }

    float4 colors[9];
    [unroll]

    for(int i = -1; i <= 1; i++) {
        [unroll]
        for(int j = -1; j <= 1; j++) {
            colors[i * 3 + j] = _texture[dispatchThreadId.xy + int2(i, j)];
        }
    }

    float guassianCenter = 0.25f;   
    float guassianSide = 0.125f;
    float guassianCorner = 0.0625f;

    float guassian[9] = {
        guassianCorner, guassianSide, guassianCorner,
        guassianSide, guassianCenter, guassianSide,
        guassianCorner, guassianSide, guassianCorner,
    };

    float3 result = float3(0, 0, 0);
    for(int i = 0; i < 9; i++) {
        result += colors[i].xyz * guassian[i];
    }

    float inv9 = 1.0f / 9.0f;

    float4 color = float4(result * inv9, 1);

    _texture[dispatchThreadId.xy] = color;
}






