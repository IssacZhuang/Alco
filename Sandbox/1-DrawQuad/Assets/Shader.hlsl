struct Vertex{
    float3 position : POSITION;
    float4 color : COLOR;
};


struct PixelInput{
    float4 position : SV_POSITION;
    float4 color : COLOR;
};

PixelInput vs_main(Vertex input){
    PixelInput output;
    output.position = float4(input.position, 1.0f);
    output.color = input.color;
    return output;
}

float4 fs_main(PixelInput input) : SV_TARGET{
    return input.color;
}
