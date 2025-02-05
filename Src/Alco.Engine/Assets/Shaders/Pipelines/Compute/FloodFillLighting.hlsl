#include "Shaders/Libs/Core.hlsli"


// light map texture
DEFINE_TEX2D_STORAGE(0, _frontBuffer, float4, "rgba16f");
DEFINE_TEX2D_STORAGE(1, _backBuffer, float4, "rgba16f");
DEFINE_UNIFORM(2, _data) {
    float attenuationSide;
    float attenuationCorner;
    int2 size;
};





float GetLightPassingFactor(uint2 pos) {
    //todo: the passing factor of wall is 0
    return 1;
}

[shader("compute")]
[numthreads(16, 16, 1)]
void MainCS(uint3 id: SV_DispatchThreadID) {
    uint2 pos = id.xy;

    float4 colors[8] = {
        _frontBuffer[id.xy + int2(1, 0)],
        _frontBuffer[id.xy + int2(-1, 0)],
        _frontBuffer[id.xy + int2(0, 1)],
        _frontBuffer[id.xy + int2(0, -1)],
        _frontBuffer[id.xy + int2(1, 1)],
        _frontBuffer[id.xy + int2(-1, 1)],
        _frontBuffer[id.xy + int2(1, -1)],
        _frontBuffer[id.xy + int2(-1, -1)],
    };



    float4 color = _frontBuffer[id.xy];
    color = max(color, colors[0] - attenuationSide);
    color = max(color, colors[1] - attenuationSide);
    color = max(color, colors[2] - attenuationSide);
    color = max(color, colors[3] - attenuationSide);
    color = max(color, colors[4] - attenuationCorner);
    color = max(color, colors[5] - attenuationCorner);
    color = max(color, colors[6] - attenuationCorner);
    color = max(color, colors[7] - attenuationCorner);



    _backBuffer[id.xy] = color * GetLightPassingFactor(id.xy);
}







