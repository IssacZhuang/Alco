#language slang 2025
module MeshScene3DSurfaceStandard;

import Core;
import AlcoRendering_MeshScene3D;

public struct Surface : IMeshScene3DSurface
{
    public typedef EmptyMeshScene3DParams Params;

    public float4 GetColor(MeshScene3DSurfaceInput input, Params p)
    {
        float4 albedo = input.TextureId != 0u
            ? meshScene3DSampleTexture(input.TextureId, input.Flags, input.Uv, input.DdxUv, input.DdyUv)
            : float4(1.0, 1.0, 1.0, 1.0);
        float lenSq = dot(input.WorldNormal, input.WorldNormal);
        float3 n = lenSq > 1e-12 ? input.WorldNormal * rsqrt(lenSq) : float3(0.0, 0.0, 1.0);
        float shade = 0.65 + 0.35 * saturate(n.z * 0.5 + 0.5);
        return float4(albedo.rgb * input.Color.rgb * shade, albedo.a * input.Color.a);
    }
}
