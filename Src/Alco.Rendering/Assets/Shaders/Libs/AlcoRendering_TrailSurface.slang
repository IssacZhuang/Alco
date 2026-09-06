// file "AlcoRendering_TrailSurface.slang" - module "AlcoRendering_TrailSurface"
//
// The surface contract between trail material modules (user-owned code,
// referenced by .amat material assets) and the trail pass template
// (GpuTrail2D). A trail material module exports exactly one public struct
// implementing ITrailSurface - the type name is free - and declares its
// textures and uniform parameters at module scope: texture slots are the
// fields of its ParameterBlocks (bound from the material asset's texture
// table by field name), uniform parameter blocks are marked [MaterialParams]
// (filled from the asset's parameter table) - the shared conventions of
// AlcoParticles_Surface, which this file mirrors.
//
// The material compiler composes the pass template with a surface module
// through slang's component system: the template's generic entry points take
// the surface type as their specialization argument (discovered from the
// surface module, exactly one conformer), so slang type-checks it against this
// contract at compile time.
//
// Division of labor: the template owns the ribbon machinery - point fetch,
// validity, the width0 -> width1 envelope, the fade in/out alpha envelope and
// the projection - so a surface only shapes the look. The vertex hook
// (getVertexAdjustment) covers the looks that move ribbon vertices (turbulence
// wander, width noise); its private VS -> FS channel (TrailVertexResult.data)
// carries what the fragment stage then needs from that math (e.g. the width
// scale's density compensation).

#language slang 2025
module AlcoRendering_TrailSurface;

import AlcoRendering_Trails2D; // TrailSample2D

/// The per-fragment inputs a trail surface shades from. color arrives as
/// lerp(color0, color1, age01) with the fade envelope's alpha already applied
/// (opacity x fadeIn x fadeOut of the trail's record).
public struct TrailSurfaceInput
{
    public float2 uv;        // x: arc length along the ribbon, y: -1..1 across
    public float4 color;     // the trail's color gradient, envelope alpha applied
    public float age;        // seconds since the point was emitted
    public float age01;      // age / life, saturated
    public float seed;       // the trail's random seed
    public float4 userData;  // the trail's custom data channel (TrailEffect2D.UserData)
    public float time;       // the renderer clock (noise animation)
    public float4 data;      // the vertex hook's private pass-through channel
};

/// The per-vertex inputs of the ribbon adjustment hook: the sampled ribbon
/// vertex and the base half width the template computed (the width0 -> width1
/// envelope over age). Fields a hook never reads are dead-code-eliminated.
public struct TrailVertexInput
{
    public TrailSample2D sample;  // posDepth/normal/u/across/age/age01/trail (envelope, colors, userData, seed)
    public float halfWidth;       // the template's base half width in world units
};

/// The vertex hook's result over the template's ribbon vertex.
public struct TrailVertexResult
{
    public float2 worldOffset;    // world-plane offset added to the vertex (0 = unchanged)
    public float widthScale;      // multiplier on the base half width (1 = unchanged)
    public float4 data;           // private VS -> FS pass-through (0 = nothing)
};

/// The trail shading contract: the fragment color of one ribbon fragment,
/// plus an optional vertex-stage ribbon adjustment hook. Both methods carry
/// defaults - a surface that only colors fragments overrides getColor alone.
/// Slang requires the `override` keyword when replacing a default
/// implementation.
public interface ITrailSurface
{
    float4 getColor(TrailSurfaceInput input) { return input.color; }

    /// The adjustment of one ribbon vertex before the projection: offsets its
    /// world position (e.g. turbulence wander), scales the ribbon width (e.g.
    /// noise bulges and necks) and fills the private pass-through the fragment
    /// stage may need from the same math (default: nothing changes).
    TrailVertexResult getVertexAdjustment(TrailVertexInput input)
    {
        TrailVertexResult result;
        result.worldOffset = float2(0.0, 0.0);
        result.widthScale = 1.0;
        result.data = float4(0.0, 0.0, 0.0, 0.0);
        return result;
    }
}
