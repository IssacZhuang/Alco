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

    /// <summary>Per-emitter render list: particle slot indices compacted by the
    /// simulate pass (diagnostics; the batched render passes no longer consume it).</summary>
    public const string RenderList = "renderList";

    /// <summary>The per-frame draw-args records of the material-batched draws
    /// (one compacted <see cref="IndexedIndirectData"/> per visible group).</summary>
    public const string DrawArgs = "drawArgs";

    /// <summary>Per-draw instance records: the instance-step vertex buffer of the batched draws.</summary>
    public const string InstanceData = "instanceData";

    /// <summary>The batched dispatch work table: one work record per 64-thread
    /// block of the wide emit/simulate dispatches (emit materials bind the emit
    /// table, simulate materials the simulate table).</summary>
    public const string WorkBlocks = "workBlocks";

    /// <summary>The group's baked color-gradient lookup texture (render pass templates).</summary>
    public const string ColorGradient = "colorGradient";

    /// <summary>The group's baked size-curve lookup texture (render pass templates).</summary>
    public const string SizeCurve = "sizeCurve";
}
