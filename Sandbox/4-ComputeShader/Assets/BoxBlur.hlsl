//dxcompiler macro
#define writeonly [[vk::ext_decorate(25)]]
#define rgba8 [[vk::image_format("rgba8")]]


Texture2D inputTexture : register(t0, space0);
writeonly rgba8 RWTexture2D<float4> outputTexture: register(u0, space1);

cbuffer Constants : register(b0, space2) { int iterations; };

[numthreads(8, 8, 1)] 
void cs_main(uint3 id: SV_DispatchThreadID) {
  // box blur
  float4 color = inputTexture.Load(id.xyz);

  int size = iterations;
  for (int i = -size; i <= size; i++) {
    for (int j = -size; j <= size; j++) {
      color = color + inputTexture.Load(id.xyz + int3(i, j, 0));
    }
  }

  color /= (2 * size + 1) * (2 * size + 1);
  outputTexture[id.xy] =  color;
}
