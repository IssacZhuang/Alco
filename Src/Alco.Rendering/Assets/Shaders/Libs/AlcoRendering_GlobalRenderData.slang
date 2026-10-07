// file "AlcoRendering_GlobalRenderData.slang" - module "AlcoRendering_GlobalRenderData"
//
// The engine-bound global render constants (set ALCO_GROUP_FRAME). Must stay
// field-compatible with Alco.Rendering.GlobalRenderData on the CPU side.

#language slang 2025
module AlcoRendering_GlobalRenderData;

public struct GlobalRenderData
{
    public float time;
    public float deltaTime;
    public float sinTime;
    public float cosTime;
    // xy: cursor in screen pixels (top-left, SV_Position space); zw reserved.
    public float4 cursor;
    // x: cutout radius in pixels (0 disables); y: soft-ring width in pixels;
    // z: max discard density in the core (1 fully clears, 0.5 = semi-transparent).
    public float4 cursorParams;
};
