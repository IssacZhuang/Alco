void ReadBlockRGBA(Texture2D<float4> SourceTexture, uint2 BasePos,
                   out float3 Block[16], out float BlockAlpha[16]) {
  for (int y = 0; y < 4; y++) {
    for (int x = 0; x < 4; x++) {
      uint2 pos = BasePos + uint2(x, y);
      float4 pixel = SourceTexture.Load(int3(pos, 0));

      int index = y * 4 + x;
      Block[index] = pixel.rgb;
      BlockAlpha[index] = pixel.a;
    }
  }
}
