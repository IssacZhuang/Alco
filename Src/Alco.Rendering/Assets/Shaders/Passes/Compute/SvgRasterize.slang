// file "SvgRasterize.slang" - module "SvgRasterize"
//
// Bindless SVG coverage rasterizer: one thread per pixel evaluates the winding
// number of every paint shape with 4x4 supersampling (fill rules: even-odd /
// non-zero), evaluates solid or gradient paint, and composites shapes in
// document order into a premultiplied rgba8 storage texture.

#language slang 2025
module SvgRasterize;

import Core;

static const int kSampleGrid = 4;
static const int kSampleCount = kSampleGrid * kSampleGrid;
static const int kRampSize = 64;

static const uint kPaintSolid = 0;
static const uint kPaintLinear = 1;
static const uint kPaintRadial = 2;

[GPURoot]
public struct SvgRasterizeRoot
{
    public GPUBufferView Edges;          // float4 (x0, y0, x1, y1)
    public GPUBufferView ShapeBboxes;    // float4 (minx, miny, maxx, maxy), 1px dilated
    public GPUBufferView ShapeMeta;      // uint4 (edgeStart, edgeCount, fillRule, paintInfo)
    public GPUBufferView ShapeColors;    // float4 straight RGBA
    public GPUBufferView GradientXforms; // float4 pairs [a,b,c,d], [e,f,fx,fy]
    public GPUBufferView GradientRamps;  // kRampSize straight RGBA per gradient
    public GPUTextureId Output;
    public uint2 TexSize;
    public uint ShapeCount;
    public uint _Pad;
};

float computeCoverage(GPUBufferView edgesView, uint edgeStart, uint edgeCount, uint fillRule, float2 pixelOrigin)
{
    int wind[kSampleCount];
    for (int k = 0; k < kSampleCount; ++k)
        wind[k] = 0;

    for (uint e = 0; e < edgeCount; ++e)
    {
        float4 edge = gpu_load<float4>(edgesView, edgeStart + e);
        float y0 = edge.y;
        float y1 = edge.w;
        int dir = y1 > y0 ? 1 : (y1 < y0 ? -1 : 0);
        if (dir == 0)
            continue;

        for (int j = 0; j < kSampleGrid; ++j)
        {
            float sy = pixelOrigin.y + (float(j) + 0.5) / float(kSampleGrid);
            bool cross = (y0 <= sy) != (y1 <= sy);
            if (!cross)
                continue;
            float x0 = edge.x;
            float x1 = edge.z;
            float t = (sy - y0) / (y1 - y0);
            float xint = x0 + t * (x1 - x0);
            for (int i = 0; i < kSampleGrid; ++i)
            {
                float sx = pixelOrigin.x + (float(i) + 0.5) / float(kSampleGrid);
                if (xint > sx)
                    wind[j * kSampleGrid + i] += dir;
            }
        }
    }

    int inside = 0;
    for (int k = 0; k < kSampleCount; ++k)
    {
        int w = wind[k];
        bool hit = fillRule == 1 ? ((w & 1) == 1) : (w != 0);
        if (hit)
            inside += 1;
    }
    return float(inside) / float(kSampleCount);
}

float4 samplePaint(SvgRasterizeRoot root, uint shapeIndex, uint paintInfo, float2 pixel)
{
    uint paintKind = paintInfo & 0x3u;
    if (paintKind == kPaintSolid)
        return gpu_load<float4>(root.ShapeColors, shapeIndex);

    float4 xf0 = gpu_load<float4>(root.GradientXforms, shapeIndex * 2u);
    float4 xf1 = gpu_load<float4>(root.GradientXforms, shapeIndex * 2u + 1u);
    float gx = pixel.x * xf0.x + pixel.y * xf0.z + xf1.x;
    float gy = pixel.x * xf0.y + pixel.y * xf0.w + xf1.y;

    float t;
    if (paintKind == kPaintLinear)
    {
        t = gy;
    }
    else
    {
        float fx = xf1.z;
        float fy = xf1.w;
        float2 d = float2(gx - fx, gy - fy);
        float dist = length(d);
        if (dist < 1e-6)
        {
            t = 0.0;
        }
        else
        {
            float2 u = d / dist;
            float2 f = float2(fx, fy);
            float fu = dot(f, u);
            float ff = dot(f, f);
            float sFar = -fu + sqrt(max(0.0, fu * fu - ff + 1.0));
            t = sFar > 1e-6 ? dist / sFar : 1.0;
        }
    }

    uint spread = (paintInfo >> 2) & 0x3u;
    if (spread == 1)
    {
        float m = frac(t * 0.5) * 2.0;
        t = m > 1.0 ? 2.0 - m : m;
    }
    else if (spread == 2)
    {
        t = frac(t);
    }
    t = clamp(t, 0.0, 1.0);

    uint rampStart = paintInfo >> 4;
    float u = t * float(kRampSize - 1);
    uint i0 = uint(u);
    uint i1 = min(i0 + 1u, uint(kRampSize - 1));
    float f = u - float(i0);
    return lerp(gpu_load<float4>(root.GradientRamps, rampStart + i0),
                gpu_load<float4>(root.GradientRamps, rampStart + i1), f);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void mainCS(uint3 id : SV_DispatchThreadID)
{
    SvgRasterizeRoot root = gpu_load_root<SvgRasterizeRoot>();
    if (any(id.xy >= root.TexSize))
        return;

    float2 pixelOrigin = float2(id.xy);
    float2 pixelCenter = pixelOrigin + 0.5;
    float4 dst = float4(0.0, 0.0, 0.0, 0.0);

    for (uint s = 0; s < root.ShapeCount; ++s)
    {
        float4 bbox = gpu_load<float4>(root.ShapeBboxes, s);
        if (pixelCenter.x < bbox.x || pixelCenter.x > bbox.z ||
            pixelCenter.y < bbox.y || pixelCenter.y > bbox.w)
            continue;

        uint4 meta = gpu_load<uint4>(root.ShapeMeta, s);
        float coverage = computeCoverage(root.Edges, meta.x, meta.y, meta.z, pixelOrigin);
        if (coverage <= 0.0)
            continue;

        float4 color = samplePaint(root, s, meta.w, pixelCenter);
        float alpha = color.a * coverage;
        float4 src = float4(color.rgb * alpha, alpha);

        dst.rgb = src.rgb + dst.rgb * (1.0 - src.a);
        dst.a = src.a + dst.a * (1.0 - src.a);
    }

    RWTexture2D<float4> output = gpu_texture<RWTexture2D<float4>>(root.Output.index);
    output[id.xy] = dst;
}
