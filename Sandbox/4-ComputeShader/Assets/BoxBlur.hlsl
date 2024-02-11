[[vk::binding(0,0)]] Texture2D<float4> inputTexture;
[[vk::image_format("rgba8")]] [[vk::binding(0,1)]] RWTexture2D<float4> outputTexture;

[numthreads(8, 8, 1)]
void cs_main(uint3 id : SV_DispatchThreadID) {
    //box blur
    float4 color = inputTexture[id.xy];
    for (int i = -1; i < 2; i++) {
        for (int j = -1; j < 2; j++) {
            int2 pos = id.xy + int2(i, j);
            color = color + inputTexture[pos];
        }
    }

    color /= 9.0;
    outputTexture[id.xy] = float4(1,1,1,1);//color;
}