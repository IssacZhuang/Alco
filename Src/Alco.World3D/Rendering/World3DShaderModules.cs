namespace Alco.World3D;

/// <summary>
/// Load names of the entry-point slang modules shipped with the Alco.World3D
/// module (the dashed file stem, per docs/SlangCodingStandard.md). Load them
/// through the engine's shader system (e.g.
/// <c>RenderingSystem.ShaderSystem.GetShader(World3DShaderModules.DeferredLighting)</c>)
/// after a file source serving this module's <c>Assets</c> folder has been
/// mounted (the module's content is copied into the application's output
/// <c>Assets</c> folder automatically when it is referenced); the module system
/// resolves each name to its source file.
/// <br/>The material-pass templates (gbuffer, shadow_depth, rsm, glass) are generic
/// over the surface type and define no directly loadable entry points — they compose
/// through the <see cref="MaterialCompiler"/> from their <see cref="ShaderLibrary"/>
/// references (see <see cref="MaterialCompiler.ComposeSurfaceShader"/>), never by
/// direct module load.
/// </summary>
public static class World3DShaderModules
{
    /// <summary>The asset folder the module's shader files live under (used by tests to enumerate them).</summary>
    public const string Folder = "Shaders/Pipelines/Rendering/PBR/";

    /// <summary>Deferred lighting pass: resolves the G-buffer into scene color.</summary>
    public const string DeferredLighting = "deferred-lighting";

    /// <summary>Volumetric light (god rays) additive overlay pass.</summary>
    public const string VolumetricLight = "volumetric-light";

    /// <summary>Horizon-based ambient occlusion pass.</summary>
    public const string HBAO = "hbao";

    /// <summary>Bilateral blur pass of the HBAO output.</summary>
    public const string HBAOBlur = "hbao-blur";

    /// <summary>Screen space reflection ray tracing pass.</summary>
    public const string SsrTrace = "screen-space-reflection-trace";

    /// <summary>Screen space reflection spatial/Temporal resolve pass.</summary>
    public const string SsrResolve = "screen-space-reflection-resolve";

    /// <summary>Screen space reflection composite pass.</summary>
    public const string SsrComposite = "screen-space-reflection-composite";

    /// <summary>Blue noise texture generator for SSR and voxel GI tracing.</summary>
    public const string SsrBlueNoise = "screen-space-reflection-blue-noise";

    /// <summary>Volumetric clouds main render pass.</summary>
    public const string VolumetricClouds = "volumetric-clouds";

    /// <summary>Volumetric clouds composite (scene blend) pass.</summary>
    public const string VolumetricCloudsComposite = "volumetric-clouds-composite";

    /// <summary>Volumetric clouds 3D noise baking pass (specialized per IsDetail at runtime).</summary>
    public const string VolumetricCloudNoise = "volumetric-cloud-noise";

    /// <summary>Volumetric clouds shadow lookup pass.</summary>
    public const string VolumetricCloudShadow = "volumetric-cloud-shadow";

    /// <summary>Voxel GI: clears the clipmap page pool.</summary>
    public const string VoxelClear = "voxel-clear";

    /// <summary>Voxel GI: direct + RSM light injection pass.</summary>
    public const string VoxelInject = "voxel-inject";

    /// <summary>Voxel GI: single-level mip filter pass.</summary>
    public const string VoxelMip = "voxel-mip";

    /// <summary>Voxel GI: whole mip chain filter pass.</summary>
    public const string VoxelMipChain = "voxel-mip-chain";

    /// <summary>Voxel GI: multi-bounce light propagation pass.</summary>
    public const string VoxelPropagate = "voxel-propagate";

    /// <summary>Voxel GI: diffuse cone tracing pass.</summary>
    public const string VoxelTrace = "voxel-trace";

    /// <summary>Voxel GI: demosaic pass of the traced probe grid.</summary>
    public const string VoxelDemosaic = "voxel-demosaic";

    /// <summary>Voxel GI: specular tracing + full-resolution upsample pass.</summary>
    public const string VoxelGiUpsample = "voxel-gi-upsample";
}
