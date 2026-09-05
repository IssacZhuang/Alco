using System.Numerics;
using Alco;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The data-only description of one trail of <see cref="GpuTrailSystem2D"/> — the
/// trail counterpart of a particle effect: geometry behavior (life, spacing, the
/// width envelope), the color gradient and the material reference. Constructible
/// in code or authored through a material asset; the system snapshots every
/// field at creation, so mutating the effect afterwards does not affect live
/// trails.
/// </summary>
public sealed class TrailEffect2D
{
    /// <summary>
    /// Gets or sets the render material asset of the trail: its surface module
    /// (implementing <c>ITrailSurface</c>) composes with the trail pass template
    /// (GpuTrail2D), its textures and parameter values bind onto the surface.
    /// Trails batch by compiled material: one multi-draw-indirect per material.
    /// Null selects the engine's default surface (the color gradient with a soft
    /// across-ribbon edge).
    /// </summary>
    public MaterialAsset? Material { get; set; }

    /// <summary>
    /// Gets or sets the blend-state override of the compiled material; null keeps
    /// the trail pass's default (<see cref="BlendState.PremultipliedAlpha"/> — the
    /// trail surfaces' output convention).
    /// </summary>
    public BlendState? Blend { get; set; }

    /// <summary>
    /// Gets or sets the depth-stencil override of the compiled material; null keeps
    /// the trail pass's default (<see cref="DepthStencilState.Read"/> — ribbons
    /// depth-test but never write).
    /// </summary>
    public DepthStencilState? Depth { get; set; }

    /// <summary>Gets or sets the seconds a trail point lives before fully dissipating.</summary>
    public float Life { get; set; } = 0.5f;

    /// <summary>Gets or sets the world-unit distance between emitted points.</summary>
    public float Spacing { get; set; } = 0.2f;

    /// <summary>Gets or sets the half width of a freshly emitted point.</summary>
    public float Width0 { get; set; } = 0.05f;

    /// <summary>Gets or sets the half width of a fully aged point.</summary>
    public float Width1 { get; set; } = 0.1f;

    /// <summary>Gets or sets the peak opacity of the ribbon body.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>
    /// Gets or sets the ribbon color of a freshly emitted point — the start of the
    /// color gradient the trail pass lerps to <see cref="Color1"/> over the point's
    /// age. The smoke surface multiplies its own color with it, so white (the
    /// default) is the identity.
    /// </summary>
    public ColorFloat Color0 { get; set; } = ColorFloat.White;

    /// <summary>
    /// Gets or sets the ribbon color of a fully aged point — the end of the color
    /// gradient (see <see cref="Color0"/>).
    /// </summary>
    public ColorFloat Color1 { get; set; } = ColorFloat.White;

    /// <summary>
    /// Gets or sets the normalized lifetime fraction over which the ribbon fades in
    /// (0 = appears at full envelope opacity, matching a particle group's fade-in).
    /// </summary>
    public float FadeIn { get; set; }

    /// <summary>
    /// Gets or sets the normalized lifetime fraction at the end over which the
    /// ribbon fades out (matching a particle group's fade-out).
    /// </summary>
    public float FadeOut { get; set; } = 0.5f;

    /// <summary>
    /// Gets or sets the expected number of simultaneously-live points of the trail,
    /// used to size its ring slice of the shared point buffer (rounded up to a power
    /// of two, clamped to the renderer's slice limits). Size it by
    /// <c>Life * peak speed / Spacing</c> — short-lived debris trails need far fewer
    /// points than long projectile ribbons, and smaller slices pack more trails into
    /// the same point budget.
    /// </summary>
    public int ExpectedPoints { get; set; } = 64;

    /// <summary>
    /// Gets or sets the material-defined custom data record of the trail, passed to
    /// the shader through the params record's userData field. The engine does not
    /// interpret it.
    /// </summary>
    public Vector4 UserData { get; set; }
}
