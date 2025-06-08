
[[vk::binding(0, 0)]] Texture2D<float> _texture;
[[vk::binding(1, 0)]] SamplerState _textureSampler; 

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

struct Textures {
    Texture2D<float> tex;
};

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  output.position = float4(input.position, 1.0f);
  output.uv = input.uv;
  return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
    Textures textures = { _texture };
    return textures.tex.Sample(_textureSampler, input.uv);
}