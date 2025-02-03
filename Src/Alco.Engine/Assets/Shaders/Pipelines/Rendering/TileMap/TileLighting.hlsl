#include "Shaders/Libs/Core.hlsli"


// light map texture
DEFINE_TEX2D_STORAGE(0, _frontBuffer, "rgba16f");
DEFINE_TEX2D_STORAGE(1, _backBuffer, "rgba16f");
DEFINE_STORAGE(2, float, _heightData);
DEFINE_UNIFORM(3, _data) {
    float attenuationSide;
    float attenuationCorner;
    int2 size;
};




float GetLightPassingFactor(float heightFrom, float heightTo) {
    return 1 - abs(heightFrom - heightTo);
}

uint GetHeightIndex(uint2 pos) {
    return pos.y * size.x + pos.x;
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

    float height = _heightData[GetHeightIndex(id.xy)];

    float neighborHeight[8] = {
        _heightData[GetHeightIndex(id.xy + int2(1, 0))],
        _heightData[GetHeightIndex(id.xy + int2(-1, 0))],
        _heightData[GetHeightIndex(id.xy + int2(0, 1))],
        _heightData[GetHeightIndex(id.xy + int2(0, -1))],
        _heightData[GetHeightIndex(id.xy + int2(1, 1))],
        _heightData[GetHeightIndex(id.xy + int2(-1, 1))],
        _heightData[GetHeightIndex(id.xy + int2(1, -1))],
        _heightData[GetHeightIndex(id.xy + int2(-1, -1))],
    };

    float4 color = _frontBuffer[id.xy];
    color = max(color, colors[0] * GetLightPassingFactor(height, neighborHeight[0]) - attenuationSide);
    color = max(color, colors[1] * GetLightPassingFactor(height, neighborHeight[1]) - attenuationSide);
    color = max(color, colors[2] * GetLightPassingFactor(height, neighborHeight[2]) - attenuationSide);
    color = max(color, colors[3] * GetLightPassingFactor(height, neighborHeight[3]) - attenuationSide);
    color = max(color, colors[4] * GetLightPassingFactor(height, neighborHeight[4]) - attenuationCorner);
    color = max(color, colors[5] * GetLightPassingFactor(height, neighborHeight[5]) - attenuationCorner);
    color = max(color, colors[6] * GetLightPassingFactor(height, neighborHeight[6]) - attenuationCorner);
    color = max(color, colors[7] * GetLightPassingFactor(height, neighborHeight[7]) - attenuationCorner);



    _backBuffer[id.xy] = color;
}







