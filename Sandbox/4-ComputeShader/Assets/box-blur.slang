// file "box-blur.slang" - module "sandbox4_box_blur"
//
// Storage image format is declared directly with [[vk::image_format]] rgba8.

#language slang 2025
module sandbox4_box_blur;

public struct InputTextureParams
{
    public Texture2D inputTexture;
};

public ParameterBlock<InputTextureParams> inputTexture;

public struct OutputTextureParams
{
    public [[vk::image_format("rgba8")]] RWTexture2D<float4> outputTexture;
};

public ParameterBlock<OutputTextureParams> outputTexture;

public struct ConstantsParams
{
    public int iterations;
};

public ParameterBlock<ConstantsParams> constants;

[shader("compute")]
[numthreads(8, 8, 1)]
void mainCS(uint3 id: SV_DispatchThreadID) {
  // box blur
  float4 color = inputTexture.inputTexture.Load(id.xyz);

  int size = constants.iterations;
  for (int i = -size; i <= size; i++) {
    for (int j = -size; j <= size; j++) {
      color = color + inputTexture.inputTexture.Load(id.xyz + int3(i, j, 0));
    }
  }

  color /= (2 * size + 1) * (2 * size + 1);
  outputTexture.outputTexture[id.xy] = color;
}
