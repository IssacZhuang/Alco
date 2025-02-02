#include "Shaders/Libs/Core.hlsli"


// light map texture
DEFINE_TEX2D_STORAGE(0, _frontBuffer, "rgba16f");
DEFINE_TEX2D_STORAGE(1, _backBuffer, "rgba16f");
DEFINE_UNIFORM(2, _data) {
    float attenuationCenter;
    float attenuationSide;
    float attenuationCorner;
};




bool IsObstacle(uint2 pos) {
    // todo: light will be blocked by wall
    return false;
}

[shader("compute")]
[numthreads(16, 16, 1)]
void MainCS(uint3 id: SV_DispatchThreadID) {
    uint2 pos = id.xy;

    if(IsObstacle(pos)) {
        _backBuffer[id.xy] = float4(0, 0, 0, 1);
        return;
    }


    float4 colors[9];

    for(int i = 0; i <= 2; i++) {
        for(int j = 0; j <= 2; j++) {
            colors[i * 3 + j] = _frontBuffer[id.xy + int2(i - 1, j - 1)];
        }
    }

    float attenuation[9] = {
        attenuationCorner, attenuationSide, attenuationCorner,
        attenuationSide, attenuationCenter, attenuationSide,
        attenuationCorner, attenuationSide, attenuationCorner,
    };


    float3 result = float3(0, 0, 0);
    for (int i = 0; i < 9; i++) {
        result += colors[i].xyz - attenuation[i];
    }

    float inv9 = 1.0f/9;

    float4 color = float4(result * inv9, 1);
    color = max(color, colors[4]);

    _backBuffer[id.xy] = color;
}







