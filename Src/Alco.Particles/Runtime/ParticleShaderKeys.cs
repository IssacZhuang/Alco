namespace Alco.Particles;

/// <summary>
/// Buffer slot names the GPU particle systems bind their pool buffers under, in both
/// behavior (compute) and surface (graphics) shaders. Module-local on purpose: these
/// keys only mean something to particle shaders, unlike the shared slots in
/// <see cref="Alco.Rendering.ShaderResourceId"/>.
/// </summary>
internal static class ParticleShaderKeys
{
    /// <summary>Per-emitter parameter array (structured buffer of EmitterParams2D/3D).</summary>
    public const string Emitters = "emitters";

    /// <summary>Per-emitter render list: particle slot indices compacted for drawing.</summary>
    public const string RenderList = "renderList";

    /// <summary>Per-emitter indirect draw-args records.</summary>
    public const string DrawArgs = "drawArgs";

    /// <summary>The group's baked color-gradient lookup texture (render pass templates).</summary>
    public const string ColorGradient = "colorGradient";

    /// <summary>The group's baked size-curve lookup texture (render pass templates).</summary>
    public const string SizeCurve = "sizeCurve";
}
