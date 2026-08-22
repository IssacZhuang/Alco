namespace Alco.World3D;

/// <summary>
/// Asset paths of the slang modules shipped with the Alco.World3D module, relative
/// to the asset root. Load them through the engine's asset system (e.g.
/// <c>AssetSystem.Load&lt;Shader&gt;(World3DAssetPaths.Shader_DeferredLighting)</c>) after a
/// file source serving this module's <c>Assets</c> folder has been mounted (the
/// module's content is copied into the application's output <c>Assets</c> folder
/// automatically when it is referenced). The asset loader derives each module's
/// identity from the file name and compiles it through the shared ShaderSystem.
/// <br/>The four material-pass templates (GBuffer, ShadowDepth, Rsm, ForwardGlass)
/// are generic over the surface type and define no entry points of their own — they
/// compose through the <see cref="MaterialCompiler"/> (see
/// <see cref="MaterialCompiler.GetTemplateShader"/>), never by direct asset load.
/// </summary>
public static class World3DAssetPaths
{
    /// <summary>The asset folder all module shader modules live under.</summary>
    public const string Folder = "ShadersSlang/Pipelines/Rendering/PBR/";

    /// <summary>The asset folder of the material pass templates (surface-generic, no entry points).</summary>
    public const string TemplateFolder = "ShadersSlang/Pipelines/";

    /// <summary>Deferred G-buffer pass template: composes with a surface (MaterialCompiler).</summary>
    public const string Shader_GBuffer = TemplateFolder + "gbuffer.slang";

    /// <summary>Deferred lighting pass: resolves the G-buffer into scene color.</summary>
    public const string Shader_DeferredLighting = Folder + "DeferredLighting.slang";

    /// <summary>Cascaded shadow map depth pass template: composes with a surface (MaterialCompiler).</summary>
    public const string Shader_ShadowDepth = TemplateFolder + "shadow_depth.slang";

    /// <summary>Reflective shadow map pass template: composes with a surface (MaterialCompiler).</summary>
    public const string Shader_Rsm = TemplateFolder + "rsm.slang";

    /// <summary>Forward transparency pass template (glass): composes with a surface (MaterialCompiler).</summary>
    public const string Shader_ForwardGlass = TemplateFolder + "glass.slang";

    /// <summary>Volumetric light (god rays) additive overlay pass.</summary>
    public const string Shader_VolumetricLight = Folder + "VolumetricLight.slang";

    /// <summary>Horizon-based ambient occlusion pass.</summary>
    public const string Shader_HBAO = Folder + "HBAO.slang";

    /// <summary>Bilateral blur pass of the HBAO output.</summary>
    public const string Shader_HBAOBlur = Folder + "HBAOBlur.slang";

    /// <summary>Screen space reflection ray tracing pass.</summary>
    public const string Shader_SsrTrace = Folder + "ScreenSpaceReflectionTrace.slang";

    /// <summary>Screen space reflection spatial/Temporal resolve pass.</summary>
    public const string Shader_SsrResolve = Folder + "ScreenSpaceReflectionResolve.slang";

    /// <summary>Screen space reflection composite pass.</summary>
    public const string Shader_SsrComposite = Folder + "ScreenSpaceReflectionComposite.slang";

    /// <summary>Blue noise texture generator for SSR and voxel GI tracing.</summary>
    public const string Shader_SsrBlueNoise = Folder + "ScreenSpaceReflectionBlueNoise.slang";

    /// <summary>Depth downsample pass feeding SSR tracing.</summary>
    public const string Shader_SsrDepthDownsample = Folder + "SsrDepthDownsample.slang";

    /// <summary>Volumetric clouds main render pass.</summary>
    public const string Shader_VolumetricClouds = Folder + "VolumetricClouds.slang";

    /// <summary>Volumetric clouds composite (scene blend) pass.</summary>
    public const string Shader_VolumetricCloudsComposite = Folder + "VolumetricCloudsComposite.slang";

    /// <summary>Volumetric clouds 3D noise baking pass.</summary>
    public const string Shader_VolumetricCloudNoise = Folder + "VolumetricCloudNoise.slang";

    /// <summary>Volumetric clouds shadow lookup pass.</summary>
    public const string Shader_VolumetricCloudShadow = Folder + "VolumetricCloudShadow.slang";

    /// <summary>Voxel GI: clears the clipmap page pool.</summary>
    public const string Shader_VoxelClear = Folder + "VoxelClear.slang";

    /// <summary>Voxel GI: surface voxelization pass.</summary>
    public const string Shader_Voxelize = Folder + "Voxelize.slang";

    /// <summary>Voxel GI: direct + RSM light injection pass.</summary>
    public const string Shader_VoxelInject = Folder + "VoxelInject.slang";

    /// <summary>Voxel GI: single-level mip filter pass.</summary>
    public const string Shader_VoxelMip = Folder + "VoxelMip.slang";

    /// <summary>Voxel GI: whole mip chain filter pass.</summary>
    public const string Shader_VoxelMipChain = Folder + "VoxelMipChain.slang";

    /// <summary>Voxel GI: multi-bounce light propagation pass.</summary>
    public const string Shader_VoxelPropagate = Folder + "VoxelPropagate.slang";

    /// <summary>Voxel GI: diffuse cone tracing pass.</summary>
    public const string Shader_VoxelTrace = Folder + "VoxelTrace.slang";

    /// <summary>Voxel GI: demosaic pass of the traced probe grid.</summary>
    public const string Shader_VoxelDemosaic = Folder + "VoxelDemosaic.slang";

    /// <summary>Voxel GI: specular tracing + full-resolution upsample pass.</summary>
    public const string Shader_VoxelGiUpsample = Folder + "VoxelGiUpsample.slang";
}
