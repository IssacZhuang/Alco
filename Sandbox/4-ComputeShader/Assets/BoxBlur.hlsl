 Texture2D<float4> inputTexture : register(t0, space0);
 RWTexture2D<float4> outputTexture : register(u0, space1);

[numthreads(8, 8, 1)]
void cs_main(uint3 id : SV_DispatchThreadID) {
    //box blur
    float4 color = inputTexture[id.xy];
    for (int i = -1; i < 2; i++) {
        for (int j = -1; j < 2; j++) {
            int2 pos = id.xy + int2(i, j);
            //color = color + inputTexture[pos];
        }
    }

    color /= 9.0;
    outputTexture[id.xy] = color;
}