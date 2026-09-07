// file "AlcoRendering_TrailSurface3D.slang" - module "AlcoRendering_TrailSurface3D"
//
// The surface contract between 3D trail material modules (user-owned code,
// referenced by .amat material assets) and the 3D trail pass template
// (GpuTrail3D) - the 3D counterpart of AlcoRendering_TrailSurface, sharing its
// conventions: a trail material module exports exactly one public struct
// implementing ITrailSurface3D - the type name is free - and declares its
// textures and uniform parameters at module scope: texture slots are the fields
// of its ParameterBlocks (bound from the material asset's texture table by field
// name), uniform parameter blocks are marked [MaterialParams] (filled from the
// asset's parameter table).
//
// The material compiler composes the pass template with a surface module through
// slang's component system: the template's generic entry points take the surface
// type as their specialization argument (discovered from the surface module,
// exactly one conformer), so slang type-checks it against this contract at
// compile time.
//
// Division of labor: the template owns the ribbon machinery - point fetch,
// validity, the camera-facing expansion, the width0 -> width1 envelope, the fade
// in/out alpha envelope and the projection - so a surface only shapes the look.
// The vertex hook (getVertexAdjustment) covers the looks that move ribbon
// vertices (turbulence wander, buoyant rise, width noise); its private VS -> FS
// channel (TrailVertex3DResult.data) carries what the fragment stage then needs
// from that math (e.g. the width scale's density compensation).

#language slang 2025
module AlcoRendering_TrailSurface3D;

import AlcoRendering_Trails3D; // TrailSample3D

/// The per-fragment inputs a 3D trail surface shades from. color arrives as
/// lerp(color0, color1, age01) with the fade envelope's alpha already applied
/// (opacity x fadeIn x fadeOut of the trail's record).
public struct TrailSurface3DInput
{
    public float2 uv;        // x: arc length along the ribbon, y: -1..1 across
    public float4 color;     // the trail's color gradient, envelope alpha applied
    public float age;        // seconds since the point was emitted
    public float age01;      // age / life, saturated
    public float seed;       // the trail's random seed
    public float4 userData;  // the trail's custom data channel (TrailEffect3D.UserData)
    public float time;       // the renderer clock (noise animation)
    public float3 worldPosition; // the shaded fragment's world position (post vertex hook)
    public float4 data;      // the vertex hook's private pass-through channel
};

/// The per-vertex inputs of the ribbon adjustment hook: the sampled ribbon
/// vertex (position/tangent/side - the camera-facing across axis) and the base
/// half width the template computed (the width0 -> width1 envelope over age).
/// Fields a hook never reads are dead-code-eliminated.
public struct TrailVertex3DInput
{
    public TrailSample3D sample;  // position/tangent/side/u/across/age/age01/trail
    public float halfWidth;       // the template's base half width in world units
};

/// The vertex hook's result over the template's ribbon vertex.
public struct TrailVertex3DResult
{
    public float3 worldOffset;    // world-space offset added to the vertex (0 = unchanged)
    public float widthScale;      // multiplier on the base half width (1 = unchanged)
    public float4 data;           // private VS -> FS pass-through (0 = nothing)
};

/// The 3D trail shading contract: the fragment color of one ribbon fragment,
/// plus an optional vertex-stage ribbon adjustment hook. Both methods carry
/// defaults - a surface that only colors fragments overrides getColor alone.
/// Slang requires the `override` keyword when replacing a default
/// implementation.
public interface ITrailSurface3D
{
    float4 getColor(TrailSurface3DInput input) { return input.color; }

    /// The adjustment of one ribbon vertex before the projection: offsets its
    /// world position (e.g. turbulence wander, buoyant rise), scales the ribbon
    /// width (e.g. noise bulges and necks) and fills the private pass-through
    /// the fragment stage may need from the same math (default: nothing changes).
    TrailVertex3DResult getVertexAdjustment(TrailVertex3DInput input)
    {
        TrailVertex3DResult result;
        result.worldOffset = float3(0.0, 0.0, 0.0);
        result.widthScale = 1.0;
        result.data = float4(0.0, 0.0, 0.0, 0.0);
        return result;
    }
}
