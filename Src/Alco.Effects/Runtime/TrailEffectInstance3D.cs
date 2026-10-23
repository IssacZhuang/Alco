using System.Numerics;

namespace Alco.Effects;

/// <summary>
/// A live trail of <see cref="GpuTrailSystem3D"/>: a GPU-resident, camera-facing ribbon of points the caller extends along a moving head.
/// After emission stops it fades and is recycled automatically, turning this object inert; disposal tears it down immediately and is a no-op on a recycled trail.
/// Main-thread only.
/// </summary>
public sealed class TrailEffectInstance3D : AutoDisposable
{
    private GpuTrailSystem3D? _renderer;
    private readonly int _slot;
    private readonly uint _generation;

    internal TrailEffectInstance3D(GpuTrailSystem3D renderer, int slot, uint generation)
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
    /// Advances the trail head to <paramref name="position"/>, emitting points spaced by <see cref="TrailEffect3D.Spacing"/>; each point records the head's travel direction as its tangent.
    /// No-op on a stopped or recycled trail.
    /// </summary>
    /// <param name="position">The world position the head moves to.</param>
    public void ExtendTo(Vector3 position)
    {
        _renderer?.ExtendTo(_slot, _generation, position);
    }

    /// <summary>
    /// Extends the trail head onto <paramref name="position"/> and stops emission, letting the trail fade; a target not ahead of the emitted path adds no backwards stub.
    /// No-op on a recycled trail.
    /// </summary>
    /// <param name="position">The final world position of the head.</param>
    public void Finish(Vector3 position)
    {
        _renderer?.Finish(_slot, _generation, position);
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
