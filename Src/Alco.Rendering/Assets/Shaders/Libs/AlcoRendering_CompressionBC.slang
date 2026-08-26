// file "AlcoRendering_CompressionBC.slang" - module "AlcoRendering_CompressionBC"
//
// upstream: github.com/su366350579/textureCompression.
//
// The two `#if defined(IS_SRGB)` variants split into dedicated *Srgb
// functions: a module compiles once, so the sRGB choice is made by the caller
// (its IS_SRGB define selects which function to call) instead of by
// recompiling the library.

#language slang 2025
module AlcoRendering_CompressionBC;

public half linearToSrgbBranchingChannel(half lin)
{
    if (lin < 0.00313067)
        return lin * 12.92;
    return pow(lin, (1.0 / 2.4)) * 1.055 - 0.055;
}
public half3 linearToSrgbBranching(half3 lin)
{
    return half3(
		linearToSrgbBranchingChannel(lin.r),
		linearToSrgbBranchingChannel(lin.g),
		linearToSrgbBranchingChannel(lin.b));
}

public half3 linearToSrgb(half3 lin)
{
	// Branching is faster than branchless on AMD on PC.
	return linearToSrgbBranching(lin);
}

// Simple convert float3 color to 565 uint using 'round' arithmetic
public uint float3ToUint565(in float3 color)
{
    float3 scale = float3(31.f, 63.f, 31.f);
    float3 colorScaled = round(saturate(color) * scale);
    uint colorPacked = ((uint) colorScaled.r << 11) | ((uint) colorScaled.g << 5) | (uint) colorScaled.b;
	
    return colorPacked;
}

// Convert float3 color to 565 uint using 'ceil' arithmetic
// color parameter is inout and is modified to match the converted value
uint float3ToUint565Ceil(inout float3 color)
{
    float3 scale = float3(31.f, 63.f, 31.f);
    float3 colorScaled = ceil(saturate(color) * scale);
    uint colorPacked = ((uint) colorScaled.r << 11) | ((uint) colorScaled.g << 5) | (uint) colorScaled.b;
    color = colorScaled / scale;

    return colorPacked;
}

// Convert float3 color to 565 uint using 'floor' arithmetic
// color parameter is inout and is modified to match the converted value
uint float3ToUint565Floor(inout float3 color)
{
    float3 scale = float3(31.f, 63.f, 31.f);
    float3 colorScaled = floor(saturate(color) * scale);
    uint colorPacked = ((uint) colorScaled.r << 11) | ((uint) colorScaled.g << 5) | (uint) colorScaled.b;
    color = colorScaled / scale;

    return colorPacked;
}

// Get min and max values in a single channel block
void getMinMax(in float Block[16], out float outMin, out float outMax)
{
    outMin = Block[0];
    outMax = Block[0];

    for (int i = 1; i < 16; ++i)
    {
        outMin = min(outMin, Block[i]);
        outMax = max(outMax, Block[i]);
    }
}

// Get min and max values in two single channel blocks
void getMinMax(in float Block0[16], in float Block1[16], out float outMin0, out float outMax0, out float outMin1, out float outMax1)
{
    outMin0 = Block0[0];
    outMax0 = Block0[0];
    outMin1 = Block1[0];
    outMax1 = Block1[0];

    for (int i = 1; i < 16; ++i)
    {
        outMin0 = min(outMin0, Block0[i]);
        outMax0 = max(outMax0, Block0[i]);
        outMin1 = min(outMin1, Block1[i]);
        outMax1 = max(outMax1, Block1[i]);
    }
}

// Get min and max values in a single three channel block
void getMinMax(in float3 Block[16], out float3 outMin, out float3 outMax)
{
    outMin = Block[0];
    outMax = Block[0];

    for (int i = 1; i < 16; ++i)
    {
        outMin = min(outMin, Block[i]);
        outMax = max(outMax, Block[i]);
    }
}

// Calculate the final packed indices for a color block
uint getPackedColorBlockIndices(in float3 Block[16], in float3 minColor, in float3 maxColor)
{
    uint packedIndices = 0;

	// Project onto max->min color vector and segment into range [0,3]
    float3 range = minColor - maxColor;
    float scale = 3.f / dot(range, range);
    float3 scaledRange = range * scale;
    float bias = (dot(maxColor, maxColor) - dot(maxColor, minColor)) * scale;
	
    for (int i = 15; i >= 0; --i)
    {
		// Compute the distance index for this element
        uint index = round(dot(Block[i], scaledRange) + bias);
		// Convert distance index into the BC index
        index += (index > 0) - (3 * (index == 3));
		// OR into the final packedIndices
        packedIndices = (packedIndices << 2) | index;
    }

    return packedIndices;
}

// Calculate the final packed indices for an alpha block
// The results are in their final location of the uint2 indices and will need ORing with the min and max alpha
uint2 getPackedAlphaBlockIndices(in float Block[16], in float minAlpha, in float maxAlpha)
{
    uint2 packedIndices = 0;

	// Segment alpha max->min into range [0,7]
    float range = minAlpha - maxAlpha;
    float scale = 7.f / range;
    float bias = -scale * maxAlpha;

    uint i = 0;
	// The first 5 elements of the block will go into the top 16 bits of the x component
    for (i = 0; i < 5; ++i)
    {
		// Compute the distance index for this element
        uint index = round(Block[i] * scale + bias);
		// Convert distance index into the BC index
        index += (index > 0) - (7 * (index == 7));
		// OR into the final packedIndices
        packedIndices.x |= (index << (i * 3 + 16));
    }

	// The 6th element is split across the x and y components
	{
        i = 5;
        uint index = round(Block[i] * scale + bias);
        index += (index > 0) - (7 * (index == 7));
        packedIndices.x |= (index << 31);
        packedIndices.y |= (index >> 1);
    }

	// The rest of the elements fill the y component
    for (i = 6; i < 16; ++i)
    {
        uint index = round(Block[i] * scale + bias);
        index += (index > 0) - (7 * (index == 7 ? 1 : 0));
        packedIndices.y |= (index << (i * 3 - 16));
    }

    return packedIndices;
}

// Compress a BC1 block
public uint2 compressBC1Block(in float3 Block[16])
{
    float3 minColor, maxColor;
    getMinMax(Block, minColor, maxColor);

    uint minColor565 = float3ToUint565Floor(minColor);
    uint maxColor565 = float3ToUint565Ceil(maxColor);

    uint indices = 0;
    if (minColor565 < maxColor565)
    {
        indices = getPackedColorBlockIndices(Block, minColor, maxColor);
    }

    return uint2((minColor565 << 16) | maxColor565, indices);
}

// Compress a BC1 block that will be sampled as sRGB
// We expect linear colors as input and convert internally
public uint2 compressBC1BlockSrgb(in float3 Block[16])
{
    float3 minColor, maxColor;
    getMinMax(Block, minColor, maxColor);

    uint minColor565 = float3ToUint565(linearToSrgb(minColor));
    uint maxColor565 = float3ToUint565(linearToSrgb(maxColor));

    uint indices = 0;
    if (minColor565 < maxColor565)
    {
        indices = getPackedColorBlockIndices(Block, minColor, maxColor);
    }

    return uint2((minColor565 << 16) | maxColor565, indices);
}

// Compress a BC3 block
public uint4 compressBC3Block(in float3 BlockRGB[16], in float blockA[16])
{
    float3 minColor, maxColor;
    getMinMax(BlockRGB, minColor, maxColor);

    uint minColor565 = float3ToUint565Floor(minColor);
    uint maxColor565 = float3ToUint565Ceil(maxColor);

    float minAlpha, maxAlpha;
    getMinMax(blockA, minAlpha, maxAlpha);

    uint minAlphaUint = (uint) round(minAlpha * 255.f);
    uint maxAlphaUint = (uint) round(maxAlpha * 255.f);

    uint colorIndices = 0;
    if (minColor565 < maxColor565)
    {
        colorIndices = getPackedColorBlockIndices(BlockRGB, minColor, maxColor);
    }

    uint2 alphaIndices = 0;
    if (minAlphaUint < maxAlphaUint)
    {
        alphaIndices = getPackedAlphaBlockIndices(blockA, minAlpha, maxAlpha);
    }

    return uint4((minAlphaUint << 8) | maxAlphaUint | alphaIndices.x, alphaIndices.y, (minColor565 << 16) | maxColor565, colorIndices);
}

// Compress a BC3 block that will be sampled as sRGB
// We expect linear colors as input and convert internally
public uint4 compressBC3BlockSrgb(in float3 BlockRGB[16], in float blockA[16])
{
    float3 minColor, maxColor;
    getMinMax(BlockRGB, minColor, maxColor);


    uint minColor565 = float3ToUint565(linearToSrgb(minColor));
    uint maxColor565 = float3ToUint565(linearToSrgb(maxColor));

    float minAlpha, maxAlpha;
    getMinMax(blockA, minAlpha, maxAlpha);

    uint minAlphaUint = (uint)round(minAlpha * 255.f);
    uint maxAlphaUint = (uint)round(maxAlpha * 255.f);

    uint colorIndices = 0;
    if (minColor565 < maxColor565)
    {
        colorIndices = getPackedColorBlockIndices(BlockRGB, minColor, maxColor);
    }

    uint2 alphaIndices = 0;
    if (minAlphaUint < maxAlphaUint)
    {
        alphaIndices = getPackedAlphaBlockIndices(blockA, minAlpha, maxAlpha);
    }

    return uint4((minAlphaUint << 8) | maxAlphaUint | alphaIndices.x, alphaIndices.y, (minColor565 << 16) | maxColor565, colorIndices);
}

// Compress a BC4 block
public uint2 compressBC4Block(in float Block[16])
{
    float minAlpha, maxAlpha;
    getMinMax(Block, minAlpha, maxAlpha);

    uint minAlphaUint = (uint) round(minAlpha * 255.f);
    uint maxAlphaUint = (uint) round(maxAlpha * 255.f);

    uint2 indices = 0;
    if (minAlphaUint < maxAlphaUint)
    {
        indices = getPackedAlphaBlockIndices(Block, minAlpha, maxAlpha);
    }

    return uint2((minAlphaUint << 8) | maxAlphaUint | indices.x, indices.y);
}

// Compress a BC5 block
public uint4 compressBC5Block(in float BlockU[16], in float BlockV[16])
{
    float minU, MaxU, MinV, MaxV;
    getMinMax(BlockU, BlockV, minU, MaxU, MinV, MaxV);

    uint minUUint = (uint) round(minU * 255.f);
    uint maxUUint = (uint) round(MaxU * 255.f);
    uint minVUint = (uint) round(MinV * 255.f);
    uint maxVUint = (uint) round(MaxV * 255.f);

    uint2 indicesU = 0;
    if (minUUint < maxUUint)
    {
        indicesU = getPackedAlphaBlockIndices(BlockU, minU, MaxU);
    }

    uint2 indicesV = 0;
    if (minVUint < maxVUint)
    {
        indicesV = getPackedAlphaBlockIndices(BlockV, MinV, MaxV);
    }

    return uint4((minUUint << 8) | maxUUint | indicesU.x, indicesU.y, (minVUint << 8) | maxVUint | indicesV.x, indicesV.y);
}

// Convert rgb linear color to YCoCG
float3 rGB2YCoCg(float3 rgb)
{
    float y = (rgb.r + 2.f * rgb.g + rgb.b) * 0.25f;
    float co = ((2.f * rgb.r - 2.f * rgb.b) * 0.25f + 128.f / 255.f);
    float cg = ((-rgb.r + 2.f * rgb.g - rgb.b) * 0.25f + 128.f / 255.f);
    return float3(y, co, cg);
}

// Get a single scale factor to use for a YCoCg color block
// This increases precision at the expense of potential blending artifacts across blocks
float2 getYCoCgScale(float2 minCoCg, float2 maxCoCg)
{
    minCoCg = abs(minCoCg - 128.f / 255.f);
    maxCoCg = abs(maxCoCg - 128.f / 255.f);

    float maxComponent = max(max(minCoCg.x, minCoCg.y), max(maxCoCg.x, maxCoCg.y));

    return (maxComponent < 32.f / 255.f) ? float2(4.f, 0.25f) : (maxComponent < 64.f / 255.f) ? float2(2.f, 0.5f) : float2(1.f, 1.f);
}

void applyYCoCgScale(inout float2 minCoCg, inout float2 maxCoCg, float scale)
{
    minCoCg = (minCoCg - 128.f / 255.f) * scale + 128.f / 255.f;
    maxCoCg = (maxCoCg - 128.f / 255.f) * scale + 128.f / 255.f;
}

// inset the coCg bounding end points
void insetCoCgEndPoints(inout float2 minCoCg, inout float2 maxCoGg)
{
    float2 inset = (maxCoGg - minCoCg - (8.f / 255.f)) / 16.f;
    minCoCg = saturate(minCoCg + inset);
    maxCoGg = saturate(maxCoGg - inset);
}

// inset the luminance end points
void insetLumaEndPoints(inout float minY, inout float maxY)
{
    float inset = (maxY - minY - (16.f / 255.f)) / 32.f;
    minY = saturate(minY + inset);
    maxY = saturate(maxY - inset);
}

// Select the 2 min/max end points from the coCg bounding rectangle based on the block contents 
void adjustMinMaxDiagonalYCoCg(const float3 Block[16], inout float2 minCoCg, inout float2 maxCoGg)
{
    float2 midCoCg = (maxCoGg + minCoCg) * 0.5;

    float sum = 0.f;
    for (int i = 0; i < 16; i++)
    {
        float2 diff = Block[i].yz - midCoCg;
        sum += diff.x * diff.y;
    }
    if (sum < 0.f)
    {
        float temp = maxCoGg.y;
        maxCoGg.y = minCoCg.y;
        minCoCg.y = temp;
    }
}

uint coCgToUint565(inout float2 coCg)
{
    float2 scale = float2(31.f, 63.f);
    float2 colorScaled = round(saturate(coCg) * scale);
    uint colorPacked = ((uint) colorScaled.r << 11) | ((uint) colorScaled.g << 5);
    coCg = colorScaled / scale;

    return colorPacked;
}

// Calculate the final packed indices for the coCg part of a color block
uint getPackedCoCgBlockIndices(in float3 Block[16], in float2 minCoCg, in float2 maxCoCg)
{
    uint packedIndices = 0;

	// Project onto max->min color vector and segment into range [0,3]
    float2 range = minCoCg - maxCoCg;
    float scale = 3.f / dot(range, range);
    float2 scaledRange = range * scale;
    float bias = (dot(maxCoCg, maxCoCg) - dot(maxCoCg, minCoCg)) * scale;

    for (int i = 15; i >= 0; --i)
    {
		// Compute the distance index for this element
        uint index = round(dot(Block[i].yz, scaledRange) + bias);
		// Convert distance index into the BC index
        index += (index > 0) - (3 * (index == 3));
		// OR into the final packedIndices
        packedIndices = (packedIndices << 2) | index;
    }

    return packedIndices;
}

// Calculate the final packed indices for the Luma part of a color block
uint2 getPackedLumaBlockIndices(in float3 Block[16], in float minAlpha, in float maxAlpha)
{
    uint2 packedIndices = 0;

	// Segment alpha max->min into range [0,7]
    float range = minAlpha - maxAlpha;
    float scale = 7.f / range;
    float bias = -scale * maxAlpha;

    uint i = 0;
	// The first 5 elements of the block will go into the top 16 bits of the x component
    for (i = 0; i < 5; ++i)
    {
		// Compute the distance index for this element
        uint index = round(Block[i].x * scale + bias);
		// Convert distance index into the BC index
        index += (index > 0) - (7 * (index == 7));
		// OR into the final packedIndices
        packedIndices.x |= (index << (i * 3 + 16));
    }

	// The 6th element is split across the x and y components
	{
        i = 5;
        uint index = round(Block[i].x * scale + bias);
        index += (index > 0) - (7 * (index == 7));
        packedIndices.x |= (index << 31);
        packedIndices.y |= (index >> 1);
    }

	// The rest of the elements fill the y component
    for (i = 6; i < 16; ++i)
    {
        uint index = round(Block[i].x * scale + bias);
        index += (index > 0) - (7 * (index == 7 ? 1 : 0));
        packedIndices.y |= (index << (i * 3 - 16));
    }

    return packedIndices;
}

// Convert a linear rgb block to YCoCg and compress a BC3 block 
public uint4 compressBC3BlockYCoCg(in float3 Block[16])
{
    for (int i = 0; i < 16; ++i)
    {
        Block[i] = rGB2YCoCg(Block[i]);
    }

    float3 minColor, maxColor;
    getMinMax(Block, minColor, maxColor);

    adjustMinMaxDiagonalYCoCg(Block, minColor.yz, maxColor.yz);

    float2 scale = getYCoCgScale(minColor.yz, maxColor.yz);

    applyYCoCgScale(minColor.yz, maxColor.yz, scale.x);

    insetCoCgEndPoints(minColor.yz, maxColor.yz);

    uint minColor565 = coCgToUint565(minColor.yz) | ((uint) scale.x - 1);
    uint maxColor565 = coCgToUint565(maxColor.yz) | ((uint) scale.x - 1);

    applyYCoCgScale(minColor.yz, maxColor.yz, scale.y);

    uint coCgIndices = getPackedCoCgBlockIndices(Block, minColor.yz, maxColor.yz);

    insetLumaEndPoints(minColor.x, maxColor.x);

    uint minLumaUint = round(minColor.x * 255.f);
    uint maxLumaUint = round(maxColor.x * 255.f);

    uint2 indices = getPackedLumaBlockIndices(Block, minColor.x, maxColor.x);

    return uint4((minLumaUint << 8) | maxLumaUint | indices.x, indices.y, (minColor565 << 16) | maxColor565, coCgIndices);
}


public void readBlockRGBA(Texture2D<float4> sourceTexture, uint2 basePos,
                   out float3 Block[16], out float BlockAlpha[16]) {
  for (int y = 0; y < 4; y++) {
    for (int x = 0; x < 4; x++) {
      uint2 pos = basePos + uint2(x, y);
      float4 pixel = sourceTexture.Load(int3(pos, 0));

      int index = y * 4 + x;
      Block[index] = pixel.rgb;
      BlockAlpha[index] = pixel.a;
    }
  }
}

