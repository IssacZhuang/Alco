#include "Rendering/ShaderLib/Core.hlslinc"

#pragma EntryCompute cs_main

DEFINE_TEX2D_READ(0, input); // should be HDR image
DEFINE_TEX2D_WRITE(1, output, "rgba8"); // should be LDR image
DEFINE_STRUCT(2, uncharted2Data){
    float A;
    float B;
    float C;
    float D;
    float E;
    float F;
    float W;
    float Exposure;
    float Gamma;
};

float3 Uncharted2Tonemap(float3 x) {
    return ((x*(A*x+C*B)+D*E)/(x*(A*x+B)+D*F)) - E/F;
}

[numthreads(16, 16, 1)]
void cs_main(uint3 id : SV_DispatchThreadID) {
    float3 hdrColor =  max(0, GET_PIXEL_TEX2D(input, id.xy).rgb - 0.004);
    float3 ldrColor = Uncharted2Tonemap(hdrColor);

    //white scale
    float3 whiteScale = 1.0 / Uncharted2Tonemap(W);
    ldrColor *= whiteScale;

    //gamma correction
    ldrColor = pow(ldrColor, 1.0 / Gamma);

    float4 color = float4(ldrColor, 1.0);
    output[id.xy] = color;
}
