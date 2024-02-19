Texture2D<float4> inputTexture : register(t0, space0);

//dxc macro
// #define readonly [[vk::ext_decorate(25)]]
// #define rgba8 [[vk::image_format("rgba8")]]

//shaderc macro
#define readonly [[spv::nonreadable]]
#define rgba8 [[spv::format_rgba8]]


readonly rgba8 RWTexture2D<float4> outputTexture: register(u0, space1);

cbuffer Constants : register(b0, space2) { int iterations; };

[numthreads(8, 8, 1)] 
void cs_main(uint3 id: SV_DispatchThreadID) {
  // box blur
   float4 color = inputTexture[int2(id.xy)];

  int size = iterations;
  for (int i = -size; i <= size; i++) {
    for (int j = -size; j <= size; j++) {
      int2 pos = int2(id.xy) + int2(i, j);
      color = color + inputTexture[pos];
    }
  }

  color /= (2 * size + 1) * (2 * size + 1);
  outputTexture[id.xy] =  color;
}
