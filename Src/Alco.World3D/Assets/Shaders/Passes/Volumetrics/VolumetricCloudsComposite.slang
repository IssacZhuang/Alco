// file "VolumetricCloudsComposite.slang" - module "VolumetricCloudsComposite"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module VolumetricCloudsComposite;

import AlcoRendering_Core;
import AlcoWorld3D_ReversedDepth;
import AlcoWorld3D_PBRCommon;

// Volumetric clouds composite pass: a full-screen overlay on the HDR scene
// color that reconstructs the half-resolution cloud result (premultiplied
// scattering + opacity, written by VolumetricClouds.slang) with a 3x3
// gaussian-bilateral upsample and blends it over the completed scene with
// premultiplied-alpha blending (One, OneMinusSrcAlpha) — the scene color is
// never bound as a texture, the blend hardware performs the composite.
//
// The spatial half of the weights (a gaussian over the continuous half-res
// texel coordinate) averages the march pass's per-pixel jitter noise into a
// fine, isotropic blur — the spatial counterpart of the temporal averaging a
// TAA-based pipeline gets for free — while the depth-similarity half keeps
// cloud edges snapped to geometry silhouettes instead of haloing. Pure-sky
// regions (all taps at the far plane) degenerate to a plain gaussian upsample.

struct Vertex
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

// This pass's resources form one ParameterBlock after AlcoWorld3D_PBRCommon's
// blocks: the half-resolution cloud march result and the shared G-buffer depth
// for the bilateral weights, joined with the same cloudData block the march
// pass binds.

// Squared gaussian width of the upsample kernel, in half-resolution texels
// (sigma ~ 0.67 texels): wide enough to average the march jitter grain into
// a fine isotropic blur, tight enough to keep cloud silhouettes crisp.
static const float CLOUD_UPSAMPLE_SIGMA2 = 0.45;


// The same cloudData block the march pass binds (RGNode_VolumetricClouds
// fills it); only marchResolutionScale and opacityDebugView are read here.
public struct CloudDataParams
{
    public float  coverage;             // 0..1 cloud coverage threshold
    public float  densityMultiplier;    // density scale
    public float  bottomAltitudeKm;     // cloud slab bottom altitude in km
    public float  slabThicknessKm;      // cloud slab thickness in km
    public float  detailStrength;       // detail erosion strength
    public float  extinctionPerKm;      // extinction coefficient 1/km
    public float  marchResolutionScale; // march resolution relative to the viewport
    public int   maxMarchSteps;        // maximum ray-march step count
    public float  windOffsetX;          // accumulated wind offset in km
    public float  windOffsetY;
    public float  timeSeconds;          // accumulated time in seconds
    public float  detailDriftPhase;     // detail noise drift phase
    public float  ambientStrength;      // ambient light strength
    public float  sunStrength;          // sun light strength
    public float  aerialFadeStartKm;    // aerial perspective fade start in km
    public float  aerialFadeEndKm;      // aerial perspective fade end in km
    public bool   opacityDebugView;     // grayscale opacity debug view
    public float  shadowExtentKm;       // shadow bake half extent in km
    public float2 shadowCenterKm;       // shadow bake window center (world xz, km)

    public Texture2D clouds;
    public DepthTexture2D gbufferDepth;
};

public ParameterBlock<CloudDataParams> cloudData;



[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    output.position = float4(input.position, 1.0f);
    output.uv = input.uv;
    return output;
}

// View-linear depth at a full-resolution uv (1e6 at the far plane — kept
// finite so the weight differences never turn into inf - inf = NaN).
float cloudCompositeLinearDepth(float2 uv)
{
    float depth = getPixelDepth2D(cloudData.gbufferDepth, int2(uv * uint2(data.viewportWidth, data.viewportHeight)));
    if (isSkyDepth(depth))
    {
        return 1e6;
    }
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float invW = dot(data.invViewProjection[3], float4(ndc, depth, 1.0));
    return abs(rcp(invW));
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET
{
    float scale = cloudData.marchResolutionScale;
    float2 halfSize = max(uint2(data.viewportWidth, data.viewportHeight) * scale, 1.0);
    float2 halfTexel = 1.0 / halfSize;

    // Continuous position of this full-resolution pixel in half-resolution
    // texel space (texel centers at n + 0.5), snapped to the nearest texel
    // center whose 3x3 neighborhood the gaussian taps below cover.
    float2 halfPos = input.position.xy * scale;
    float2 center = floor(halfPos) + 0.5;

    float centerDepth = cloudCompositeLinearDepth(input.uv);
    // Depth tolerance grows with distance: nearby geometry gets tight edges,
    // distant silhouettes still aggregate their sky taps.
    float sigma = max(centerDepth, 1.0) * 0.03;

    float3 scattering = 0.0;
    float opacity = 0.0;
    float weightSum = 0.0;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 tapPos = clamp(center + float2(x, y), 0.5, halfSize - 0.5);
            float2 tapUv = tapPos * halfTexel;

            // Spatial gaussian over the sub-texel distance: averages out the
            // march jitter grain.
            float2 delta = halfPos - tapPos;
            float spatialWeight = exp(-0.5 * dot(delta, delta) / CLOUD_UPSAMPLE_SIGMA2);
            float tapDepth = cloudCompositeLinearDepth(tapUv);
            float depthWeight = exp(-abs(tapDepth - centerDepth) / sigma);

            float w = spatialWeight * depthWeight + 1e-5;
            float4 tap = cloudData.clouds.Sample(samplers.linearClamp, tapUv);
            scattering += tap.rgb * w;
            opacity += tap.a * w;
            weightSum += w;
        }
    }

    scattering /= weightSum;
    opacity /= weightSum;

    if (cloudData.opacityDebugView)
    {
        // Opacity debug view: replace the scene with the coverage mask.
        return float4(opacity.xxx, 1.0);
    }
    return float4(scattering, opacity);
}
