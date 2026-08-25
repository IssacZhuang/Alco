// file "box-blur.slang" - module "sandbox4_box_blur"
//
// Slang module converted from BoxBlur.hlsl (Phase 4 migration): the low-level
// sandbox compiles it through the native slang module API. The dxc-era
// writeonly/rgba8 extension macros became native declarations — storage image
// formats are spelled with [[vk::image_format]] directly.

#language slang 2025
module sandbox4_box_blur;

public struct InputTextureParams
{
    public Texture2D inputTexture;
};

public ParameterBlock<InputTextureParams> _inputTexture;

public struct OutputTextureParams
{
    public [[vk::image_format("rgba8")]] RWTexture2D<float4> outputTexture;
};

public ParameterBlock<OutputTextureParams> _outputTexture;

public struct ConstantsParams
{
    public int iterations;
};

public ParameterBlock<ConstantsParams> _constants;

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 id: SV_DispatchThreadID) {
  // box blur
  float4 color = _inputTexture.inputTexture.Load(id.xyz);

  int size = _constants.iterations;
  for (int i = -size; i <= size; i++) {
    for (int j = -size; j <= size; j++) {
      color = color + _inputTexture.inputTexture.Load(id.xyz + int3(i, j, 0));
    }
  }

  color /= (2 * size + 1) * (2 * size + 1);
  _outputTexture.outputTexture[id.xy] = color;
}
