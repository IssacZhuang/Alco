#language slang 2025
module AlcoGUI_Pass;

import Core;
import SharedSampler;
import AlcoGUI_Surface;

[GPURoot]
public struct UIPassRoot<T : IUISurface>
{
    public UIPassHeader Pass;
    public T.Params Material;
};

[shader("vertex")]
UIVsOut vsMain<T : IUISurface>(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    UIPassRoot<T> root = gpu_load_root<UIPassRoot<T>>();
    UIInstance inst = gpu_load<UIInstance>(root.Pass.Quads, instanceId);
    return uiExpandQuadVertex(inst, root.Pass.ClipScale, vertexId);
}

[shader("pixel")]
float4 psMain<T : IUISurface>(UIVsOut input) : SV_Target0
{
    UIPassRoot<T> root = gpu_load_root<UIPassRoot<T>>();
    UISurfaceInput surf;
    surf.Uv = input.Uv;
    surf.LocalUv = input.LocalUv;
    surf.Color = input.Color;
    surf.BaseColor = uiShade(input);
    surf.TextureIndex = input.TextureIndex;
    surf.Kind = input.Kind;

    T s = T();
    return s.Shade(surf, root.Material);
}
