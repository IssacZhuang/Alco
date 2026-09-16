using System.Numerics;

namespace Alco.Rendering;

public struct GlobalRenderData
{
    public float Time;
    public float DeltaTime;
    public float SinTime;
    public float CosTime;

    /// <summary>
    /// Cursor data: xy is the cursor position in screen pixels (top-left origin, the
    /// SV_Position space); zw reserved. Consumers opt in per material (e.g. tree
    /// canopies); the game writes it every frame. See also <see cref="CursorParams"/>.
    /// </summary>
    public Vector4 Cursor;

    /// <summary>
    /// Cursor-cutout parameters: x the circle radius in screen pixels (0 disables the
    /// cutout), y the soft-ring width the discard density ramps across, z the maximum
    /// discard density in the circle core (1 fully clears, 0.5 reads as semi-transparent),
    /// w reserved.
    /// </summary>
    public Vector4 CursorParams;
}

