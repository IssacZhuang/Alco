struct Vertex{
    float3 position : POSITION;
    float4 color : COLOR;
};


struct PixelInput{
    float4 position : SV_POSITION;
};

[[vk_binding(0,0)]]cbuffer color{
    float4 color;
}

[shader("vertex")]
PixelInput MainVS(Vertex input) {
    PixelInput output;
    output.position = float4(input.position, 1.0f);
    return output;
}

[shader("pixel")]
float4 MainPS(PixelInput input) : SV_TARGET {
    return color;
}
