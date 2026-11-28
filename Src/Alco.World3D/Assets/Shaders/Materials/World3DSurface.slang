#language slang 2025
module World3DSurface;

import World3DSceneData;

/// Fragment inputs shared by the built-in PBR surface and custom surfaces.
public struct SurfaceInput
{
    public float3 EyeRelativePosition;
    public float3 NormalWS;
    public float4 TangentWS; // xyz: orthonormal tangent; w: bitangent sign.
    public float2 UV;
    public float2 DdxUV;
    public float2 DdyUV;
    public World3DMaterialGpu Material;
};

/// Inputs supplied by GPU voxelization when evaluating a surface inside a compute kernel.
public struct VoxelSurfaceInput
{
    public float3 AnchorRelativePosition; // Position relative to OriginAnchorCell.
    public float3 NormalWS;
    public float2 UV;
    public float TextureLod;
    public World3DMaterialGpu Material;
};

/// Surface hook evaluated during GPU voxelization without quad derivatives.
public interface IVoxelFeedSurface
{
    /// Linear albedo (rgb) and coverage (a), before instance fade.
    float4 getVoxelBaseColor(VoxelSurfaceInput input) { return input.Material.BaseColor; }
    /// Linear emissive radiance.
    float3 getVoxelEmissive(VoxelSurfaceInput input) { return input.Material.EmissiveAlphaCutoff.rgb; }
};

// Export one public struct implementing ISurface. Override only the needed
// methods; the defaults describe the textureless material from the scene table.
public interface ISurface : IVoxelFeedSurface
{
    // Both hooks receive the same pre-deformation eye-relative position and normal.
    float3 getWorldPosition(float3 eyeRelativePosition, float3 normalWS, float2 uv) { return eyeRelativePosition; }
    float3 getWorldNormal(float3 eyeRelativePosition, float3 normalWS, float2 uv) { return normalWS; }

    /// Linear albedo (rgb) and coverage (a), before the instance fade.
    float4 getBaseColor(SurfaceInput input) { return input.Material.BaseColor; }
    /// Normal in the supplied tangent frame.
    float3 getNormalTS(SurfaceInput input) { return float3(0.0, 0.0, 1.0); }
    /// Metallic, perceptual roughness and ambient occlusion, respectively.
    float3 getMetallicRoughnessAO(SurfaceInput input) { return float3(input.Material.MetallicRoughness, 1.0); }
    /// Linear emissive radiance.
    float3 getEmissive(SurfaceInput input) { return input.Material.EmissiveAlphaCutoff.rgb; }
};
