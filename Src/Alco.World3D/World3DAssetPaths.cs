namespace Alco.World3D;

/// <summary>
/// Asset paths of the shaders shipped with the Alco.World3D module, relative to
/// the asset root. Load them through the engine's asset system (e.g.
/// <c>AssetSystem.Load&lt;Shader&gt;(World3DAssetPaths.Shader_GBuffer)</c>) after a
/// file source serving this module's <c>Assets</c> folder has been mounted (the
/// module's content is copied into the application's output <c>Assets</c> folder
/// automatically when it is referenced).
/// </summary>
public static class World3DAssetPaths
{
    /// <summary>The asset folder all module shaders live under.</summary>
    public const string Folder = "Shaders/Pipelines/Rendering/PBR/";

    /// <summary>Deferred G-buffer pass: unpacks PBR materials into albedo / normal / metallic-roughness-AO / emissive.</summary>
    public const string Shader_GBuffer = Folder + "GBuffer.hlsl";

    /// <summary>Deferred lighting pass: resolves the G-buffer into scene color.</summary>
    public const string Shader_DeferredLighting = Folder + "DeferredLighting.hlsl";

    /// <summary>Cascaded shadow map depth pass.</summary>
    public const string Shader_ShadowDepth = Folder + "ShadowDepth.hlsl";

    /// <summary>Reflective shadow map pass (sun bounce GI input).</summary>
    public const string Shader_Rsm = Folder + "Rsm.hlsl";

    /// <summary>Forward transparency pass (glass).</summary>
    public const string Shader_ForwardGlass = Folder + "ForwardGlass.hlsl";

    /// <summary>Volumetric light (god rays) additive overlay pass.</summary>
    public const string Shader_VolumetricLight = Folder + "VolumetricLight.hlsl";

    /// <summary>Horizon-based ambient occlusion pass.</summary>
    public const string Shader_HBAO = Folder + "HBAO.hlsl";

    /// <summary>Bilateral blur pass of the HBAO output.</summary>
    public const string Shader_HBAOBlur = Folder + "HBAOBlur.hlsl";

    /// <summary>Screen space reflection ray tracing pass.</summary>
    public const string Shader_SsrTrace = Folder + "ScreenSpaceReflectionTrace.hlsl";

    /// <summary>Screen space reflection spatial/Temporal resolve pass.</summary>
    public const string Shader_SsrResolve = Folder + "ScreenSpaceReflectionResolve.hlsl";

    /// <summary>Screen space reflection composite pass.</summary>
    public const string Shader_SsrComposite = Folder + "ScreenSpaceReflectionComposite.hlsl";

    /// <summary>Blue noise texture generator for SSR and voxel GI tracing.</summary>
    public const string Shader_SsrBlueNoise = Folder + "ScreenSpaceReflectionBlueNoise.hlsl";

    /// <summary>Depth downsample pass feeding SSR tracing.</summary>
    public const string Shader_SsrDepthDownsample = Folder + "SsrDepthDownsample.hlsl";

    /// <summary>Volumetric clouds main render pass.</summary>
    public const string Shader_VolumetricClouds = Folder + "VolumetricClouds.hlsl";

    /// <summary>Volumetric clouds composite (scene blend) pass.</summary>
    public const string Shader_VolumetricCloudsComposite = Folder + "VolumetricCloudsComposite.hlsl";

    /// <summary>Volumetric clouds 3D noise baking pass.</summary>
    public const string Shader_VolumetricCloudNoise = Folder + "VolumetricCloudNoise.hlsl";

    /// <summary>Volumetric clouds shadow lookup pass.</summary>
    public const string Shader_VolumetricCloudShadow = Folder + "VolumetricCloudShadow.hlsl";

    /// <summary>Voxel GI: clears the clipmap page pool.</summary>
    public const string Shader_VoxelClear = Folder + "VoxelClear.hlsl";

    /// <summary>Voxel GI: surface voxelization pass.</summary>
    public const string Shader_Voxelize = Folder + "Voxelize.hlsl";

    /// <summary>Voxel GI: direct + RSM light injection pass.</summary>
    public const string Shader_VoxelInject = Folder + "VoxelInject.hlsl";

    /// <summary>Voxel GI: single-level mip filter pass.</summary>
    public const string Shader_VoxelMip = Folder + "VoxelMip.hlsl";

    /// <summary>Voxel GI: whole mip chain filter pass.</summary>
    public const string Shader_VoxelMipChain = Folder + "VoxelMipChain.hlsl";

    /// <summary>Voxel GI: multi-bounce light propagation pass.</summary>
    public const string Shader_VoxelPropagate = Folder + "VoxelPropagate.hlsl";

    /// <summary>Voxel GI: diffuse cone tracing pass.</summary>
    public const string Shader_VoxelTrace = Folder + "VoxelTrace.hlsl";

    /// <summary>Voxel GI: demosaic pass of the traced probe grid.</summary>
    public const string Shader_VoxelDemosaic = Folder + "VoxelDemosaic.hlsl";

    /// <summary>Voxel GI: specular tracing + full-resolution upsample pass.</summary>
    public const string Shader_VoxelGiUpsample = Folder + "VoxelGiUpsample.hlsl";
}
