// file "SvgRasterize.slang" - module "SvgRasterize"
//
// SVG coverage rasterizer: one thread per pixel evaluates the winding number of
// every paint shape with 4x4 supersampling (fill rules: even-odd / non-zero),
// evaluates the shape's paint (solid color or baked gradient ramp) and composites
// the shapes in document order into a premultiplied rgba8 output texture.
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module SvgRasterize;

import AlcoRendering_Core;

static const int kSampleGrid = 4;              // 4x4 supersamples per pixel
static const int kSampleCount = kSampleGrid * kSampleGrid;
static const int kRampSize = 64;               // must match SvgRasterizeData.GradientRampSize

// Paint kinds encoded in shapeMeta.w (see SvgRasterizeData).
static const uint kPaintSolid = 0;
static const uint kPaintLinear = 1;
static const uint kPaintRadial = 2;

struct Constants {
    uint2 texSize;
    uint shapeCount;
    uint _pad;
};

public struct EdgeParams
{
    public RWStructuredBuffer<float4> edges;       // (x0, y0, x1, y1)
};
public ParameterBlock<EdgeParams> edges;

public struct BboxParams
{
    public RWStructuredBuffer<float4> shapeBboxes; // (minx, miny, maxx, maxy), 1px dilated
};
public ParameterBlock<BboxParams> shapeBboxes;

public struct MetaParams
{
    public RWStructuredBuffer<uint4> shapeMeta;    // (edgeStart, edgeCount, fillRule, paintInfo)
};
public ParameterBlock<MetaParams> shapeMeta;

public struct ColorParams
{
    public RWStructuredBuffer<float4> shapeColors; // straight RGBA
};
public ParameterBlock<ColorParams> shapeColors;

public struct GradientXformParams
{
    public RWStructuredBuffer<float4> gradientXforms; // 2 per shape: [a b c d], [e f fx fy]
};
public ParameterBlock<GradientXformParams> gradientXforms;

public struct RampParams
{
    public RWStructuredBuffer<float4> gradientRamps;  // kRampSize straight RGBA per gradient
};
public ParameterBlock<RampParams> gradientRamps;

public struct OutputParams
{
    public [[vk::image_format("rgba8")]] RWTexture2D<float4> output;
};
public ParameterBlock<OutputParams> output;

[[vk::push_constant]] Constants constants;

/// Winding coverage of one shape at a pixel, sampled kSampleGrid x kSampleGrid times.
float computeCoverage(uint edgeStart, uint edgeCount, uint fillRule, float2 pixelOrigin)
{
    int wind[kSampleCount];
    for (int k = 0; k < kSampleCount; ++k) {
        wind[k] = 0;
    }

    for (uint e = 0; e < edgeCount; ++e) {
        float4 edge = edges.edges[edgeStart + e];
        float y0 = edge.y;
        float y1 = edge.w;
        int dir = y1 > y0 ? 1 : (y1 < y0 ? -1 : 0);
        if (dir == 0) {
            continue;
        }

        for (int j = 0; j < kSampleGrid; ++j) {
            float sy = pixelOrigin.y + (float(j) + 0.5) / float(kSampleGrid);
            // Half-open span test: an edge owning [min(y0,y1), max(y0,y1)).
            bool cross = (y0 <= sy) != (y1 <= sy);
            if (!cross) {
                continue;
            }
            float x0 = edge.x;
            float x1 = edge.z;
            float t = (sy - y0) / (y1 - y0);
            float xint = x0 + t * (x1 - x0);
            for (int i = 0; i < kSampleGrid; ++i) {
                float sx = pixelOrigin.x + (float(i) + 0.5) / float(kSampleGrid);
                if (xint > sx) {
                    wind[j * kSampleGrid + i] += dir;
                }
            }
        }
    }

    int inside = 0;
    for (int k = 0; k < kSampleCount; ++k) {
        int w = wind[k];
        bool hit = fillRule == 1 ? ((w & 1) == 1) : (w != 0);
        if (hit) {
            inside += 1;
        }
    }
    return float(inside) / float(kSampleCount);
}

/// Gradient parameter t at a pixel (device space), then spread-adjusted ramp lookup.
float4 samplePaint(uint shapeIndex, uint paintInfo, float2 pixel)
{
    uint paintKind = paintInfo & 0x3u;
    if (paintKind == kPaintSolid) {
        return shapeColors.shapeColors[shapeIndex];
    }

    float4 xf0 = gradientXforms.gradientXforms[shapeIndex * 2u];
    float4 xf1 = gradientXforms.gradientXforms[shapeIndex * 2u + 1u];
    float gx = pixel.x * xf0.x + pixel.y * xf0.z + xf1.x;
    float gy = pixel.x * xf0.y + pixel.y * xf0.w + xf1.y;

    float t;
    if (paintKind == kPaintLinear) {
        t = gy;
    } else {
        // Radial with focal point (SVG 1.1): t = |P-F| / |P'-F| where P' is the ray
        // intersection with the unit circle. Degenerates to |P| when F is (0, 0).
        float fx = xf1.z;
        float fy = xf1.w;
        float2 d = float2(gx - fx, gy - fy);
        float dist = length(d);
        if (dist < 1e-6) {
            t = 0.0;
        } else {
            float2 u = d / dist;
            float2 f = float2(fx, fy);
            float fu = dot(f, u);
            float ff = dot(f, f);
            float sFar = -fu + sqrt(max(0.0, fu * fu - ff + 1.0));
            t = sFar > 1e-6 ? dist / sFar : 1.0;
        }
    }

    // Spread: 0 pad, 1 reflect, 2 repeat.
    uint spread = (paintInfo >> 2) & 0x3u;
    if (spread == 1) {
        float m = frac(t * 0.5) * 2.0;
        t = m > 1.0 ? 2.0 - m : m;
    } else if (spread == 2) {
        t = frac(t);
    }
    t = clamp(t, 0.0, 1.0);

    uint rampStart = paintInfo >> 4;
    float u = t * float(kRampSize - 1);
    uint i0 = uint(u);
    uint i1 = min(i0 + 1u, uint(kRampSize - 1));
    float f = u - float(i0);
    return lerp(gradientRamps.gradientRamps[rampStart + i0],
                gradientRamps.gradientRamps[rampStart + i1], f);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void mainCS(uint3 id : SV_DispatchThreadID) {
    if (any(id.xy >= constants.texSize)) {
        return;
    }

    float2 pixelOrigin = float2(id.xy);
    float2 pixelCenter = pixelOrigin + 0.5;

    float4 dst = float4(0.0, 0.0, 0.0, 0.0); // premultiplied accumulator

    for (uint s = 0; s < constants.shapeCount; ++s) {
        float4 bbox = shapeBboxes.shapeBboxes[s];
        if (pixelCenter.x < bbox.x || pixelCenter.x > bbox.z ||
            pixelCenter.y < bbox.y || pixelCenter.y > bbox.w) {
            continue;
        }

        uint4 meta = shapeMeta.shapeMeta[s];
        float coverage = computeCoverage(meta.x, meta.y, meta.z, pixelOrigin);
        if (coverage <= 0.0) {
            continue;
        }

        float4 color = samplePaint(s, meta.w, pixelCenter); // straight RGBA
        float alpha = color.a * coverage;
        float4 src = float4(color.rgb * alpha, alpha);      // premultiply

        // Source-over in premultiplied space, document order: an opaque layer
        // replaces, a translucent layer blends over what is already there.
        dst.rgb = src.rgb + dst.rgb * (1.0 - src.a);
        dst.a = src.a + dst.a * (1.0 - src.a);
    }

    output.output[id.xy] = dst;
}
