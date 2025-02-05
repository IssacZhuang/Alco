void ReadBlockRGBA(Texture2D<float4> SourceTexture, SamplerState TextureSampler,
                   float2 UV, float2 TexelUVSize, out float3 Block[16],
                   out float BlockAlpha[16]) {
  {
    float4 Red = SourceTexture.GatherRed(TextureSampler, UV);
    float4 Green = SourceTexture.GatherGreen(TextureSampler, UV);
    float4 Blue = SourceTexture.GatherBlue(TextureSampler, UV);
    float4 Alpha = SourceTexture.GatherAlpha(TextureSampler, UV);
    Block[0] = float3(Red[3], Green[3], Blue[3]);
    Block[1] = float3(Red[2], Green[2], Blue[2]);
    Block[4] = float3(Red[0], Green[0], Blue[0]);
    Block[5] = float3(Red[1], Green[1], Blue[1]);
    BlockAlpha[0] = Alpha[3];
    BlockAlpha[1] = Alpha[2];
    BlockAlpha[4] = Alpha[0];
    BlockAlpha[5] = Alpha[1];
  }
  {
    float2 UVOffset = UV + float2(2.f * TexelUVSize.x, 0);
    float4 Red = SourceTexture.GatherRed(TextureSampler, UVOffset);
    float4 Green = SourceTexture.GatherGreen(TextureSampler, UVOffset);
    float4 Blue = SourceTexture.GatherBlue(TextureSampler, UVOffset);
    float4 Alpha = SourceTexture.GatherAlpha(TextureSampler, UV);
    Block[2] = float3(Red[3], Green[3], Blue[3]);
    Block[3] = float3(Red[2], Green[2], Blue[2]);
    Block[6] = float3(Red[0], Green[0], Blue[0]);
    Block[7] = float3(Red[1], Green[1], Blue[1]);
    BlockAlpha[2] = Alpha[3];
    BlockAlpha[3] = Alpha[2];
    BlockAlpha[6] = Alpha[0];
    BlockAlpha[7] = Alpha[1];
  }
  {
    float2 UVOffset = UV + float2(0, 2.f * TexelUVSize.y);
    float4 Red = SourceTexture.GatherRed(TextureSampler, UVOffset);
    float4 Green = SourceTexture.GatherGreen(TextureSampler, UVOffset);
    float4 Blue = SourceTexture.GatherBlue(TextureSampler, UVOffset);
    float4 Alpha = SourceTexture.GatherAlpha(TextureSampler, UV);
    Block[8] = float3(Red[3], Green[3], Blue[3]);
    Block[9] = float3(Red[2], Green[2], Blue[2]);
    Block[12] = float3(Red[0], Green[0], Blue[0]);
    Block[13] = float3(Red[1], Green[1], Blue[1]);
    BlockAlpha[8] = Alpha[3];
    BlockAlpha[9] = Alpha[2];
    BlockAlpha[12] = Alpha[0];
    BlockAlpha[13] = Alpha[1];
  }
  {
    float2 UVOffset = UV + 2.f * TexelUVSize;
    float4 Red = SourceTexture.GatherRed(TextureSampler, UVOffset);
    float4 Green = SourceTexture.GatherGreen(TextureSampler, UVOffset);
    float4 Blue = SourceTexture.GatherBlue(TextureSampler, UVOffset);
    float4 Alpha = SourceTexture.GatherAlpha(TextureSampler, UV);
    Block[10] = float3(Red[3], Green[3], Blue[3]);
    Block[11] = float3(Red[2], Green[2], Blue[2]);
    Block[14] = float3(Red[0], Green[0], Blue[0]);
    Block[15] = float3(Red[1], Green[1], Blue[1]);
    BlockAlpha[10] = Alpha[3];
    BlockAlpha[11] = Alpha[2];
    BlockAlpha[14] = Alpha[0];
    BlockAlpha[15] = Alpha[1];
  }
}