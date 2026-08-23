// file "imgui.slang" - module "imgui"
//
// Slang module converted from ImGui.hlsl (Phase 2 migration): compiled
// through the native slang module API from the embedded resource. The vertex
// color arrives as Unorm8x4 and converts to float4 at the input boundary; the
// C# side supplies the custom vertex layout (see ImGUIResourceHelper).

#language slang 2025
module imgui;

struct Vertex {
  float2 position : POSITION;
  float2 uv : TEXCOORD0;
  float4 color : COLOR;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  float4 color : COLOR;
};

[[vk::binding(0, 0)]] cbuffer _camera { float4x4 viewProjection; };

[[vk::binding(0, 1)]] Texture2D _texture;
[[vk::binding(1, 1)]] SamplerState _textureSampler;

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  output.position = mul(viewProjection, float4(input.position, 0.0f, 1.0f));
  output.uv = input.uv;
  output.color = input.color;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  float4 color = _texture.Sample(_textureSampler, input.uv);
  return color * input.color;
}
