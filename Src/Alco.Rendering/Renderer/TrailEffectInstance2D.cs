using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// A live trail of <see cref="GpuTrailSystem2D"/>: a GPU-resident ribbon of points
/// the caller extends along a moving head. Created through
/// <see cref="GpuTrailSystem2D.TryCreateInstance"/>. Emission is the only per-point
/// CPU work; aging, validity and ribbon expansion run in the vertex stage.
/// <br/>Lifecycle: the trail fades on its own once emission stops — when every point
/// has outlived its age budget the renderer recycles the trail's slot and slice, and
/// this object turns inert (every member becomes a harmless no-op). Disposing the
/// instance instead tears the trail down immediately, points vanishing mid-flight;
/// both paths release the same pooled resources, so a recycled trail's later
/// disposal is a no-op.
/// <br/>Threading: main-thread contract, like the renderer itself.
/// </summary>
public sealed class TrailEffectInstance2D : AutoDisposable
{
    private GpuTrailSystem2D? _renderer;
    private readonly int _slot;
    private readonly uint _generation;

    internal TrailEffectInstance2D(GpuTrailSystem2D renderer, int slot, uint generation)
    {
        _renderer = renderer;
        _slot = slot;
        _generation = generation;
    }

    /// <summary>Whether the trail still emits points (starts emitting on creation; <see cref="Stop"/> ends it).</summary>
    public bool IsEmitting => _renderer?.GetEmitting(_slot, _generation) ?? false;

    /// <summary>
    /// Whether the trail still does work: emitting, or has points that have not fully
    /// dissipated. A recycled trail reports false.
    /// </summary>
    public bool IsAlive => _renderer?.GetAlive(_slot, _generation) ?? false;

    /// <summary>
    /// Whether the trail draws (culling hook driven by the caller). An invisible
    /// trail drops out of the draw plan — its points keep aging, so it fades while
    /// hidden instead of bursting when shown again.
    /// </summary>
    public bool IsVisible
    {
        get => _renderer?.GetVisible(_slot, _generation) ?? false;
        set => _renderer?.SetVisible(_slot, _generation, value);
    }

    /// <summary>
    /// Advances the trail head to <paramref name="position"/>, emitting points spaced
    /// by the trail's <see cref="TrailEffect2D.Spacing"/> along the way. The ribbon
    /// normal of a straight run is the perpendicular of the head's travel direction.
    /// No-op on a stopped or recycled trail.
    /// </summary>
    /// <param name="position">The world position the head moves to (2.5D games bake their height into y).</param>
    /// <param name="depth">The render depth baked into the emitted points (the game's depth-stack convention).</param>
    public void ExtendTo(Vector2 position, float depth)
    {
        _renderer?.ExtendTo(_slot, _generation, position, depth);
    }

    /// <summary>
    /// Extends the trail head onto <paramref name="position"/> (e.g. the destruction
    /// point of the trail's source) and stops emission, letting the trail fade.
    /// Without it the ribbon would stop at the last <see cref="ExtendTo"/> position,
    /// visibly short of the impact. A target not ahead of the emitted path adds no
    /// backwards stub. No-op on a recycled trail.
    /// </summary>
    /// <param name="position">The final world position of the head.</param>
    /// <param name="depth">The render depth of the final segment.</param>
    public void Finish(Vector2 position, float depth)
    {
        _renderer?.Finish(_slot, _generation, position, depth);
    }

    /// <summary>Stops emission; the live points fade out naturally.</summary>
    public void Stop()
    {
        _renderer?.Stop(_slot, _generation);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Hard teardown: the slot and slice return to the pool immediately and
            // the points vanish. Safe after a fade-out recycle — the generation check
            // inside makes it a no-op then.
            _renderer?.ReleaseTrail(_slot, _generation);
            _renderer = null;
        }
    }
}
