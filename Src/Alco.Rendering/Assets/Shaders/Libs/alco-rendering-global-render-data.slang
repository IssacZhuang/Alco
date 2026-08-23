// file "alco-rendering-global-render-data.slang" - module "alco-rendering-global-render-data"
//
// Slang port of Shaders/Libs/GlobalRenderData.hlsli: the engine-bound global
// render constants (set ALCO_GROUP_FRAME). Must stay field-compatible with
// Alco.Rendering.GlobalRenderData on the CPU side.

#language slang 2025
module alco_rendering_global_render_data;

public struct GlobalRenderData
{
    public float time;
    public float deltaTime;
    public float sinTime;
    public float cosTime;
};
