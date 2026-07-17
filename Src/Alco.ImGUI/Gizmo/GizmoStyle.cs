namespace Alco.ImGUI;

/// <summary>
/// Visual style of the manipulation gizmo: colors, handle sizes and line widths.
/// Defaults match the ImGuizmo reference implementation. Colors are packed ImU32
/// values (R | G &lt;&lt; 8 | B &lt;&lt; 16 | A &lt;&lt; 24) as accepted by ImDrawList.
/// </summary>
public sealed class GizmoStyle
{
    /// <summary>Thickness of translation axis lines in pixels.</summary>
    public float TranslationLineThickness { get; set; } = 3.0f;

    /// <summary>Size of the translation axis arrow heads in pixels.</summary>
    public float TranslationLineArrowSize { get; set; } = 6.0f;

    /// <summary>Thickness of rotation ring lines in pixels.</summary>
    public float RotationLineThickness { get; set; } = 2.0f;

    /// <summary>Thickness of the outer (screen-space) rotation ring in pixels.</summary>
    public float RotationOuterLineThickness { get; set; } = 3.0f;

    /// <summary>Thickness of scale axis lines in pixels.</summary>
    public float ScaleLineThickness { get; set; } = 3.0f;

    /// <summary>Radius of the scale axis end markers in pixels.</summary>
    public float ScaleLineCircleSize { get; set; } = 6.0f;

    /// <summary>Thickness of the hatched lines drawn on flipped axes in pixels.</summary>
    public float HatchedAxisLineThickness { get; set; } = 6.0f;

    /// <summary>Radius of the gizmo center circle in pixels.</summary>
    public float CenterCircleSize { get; set; } = 6.0f;

    /// <summary>Color of the X axis handle.</summary>
    public uint DirectionXColor { get; set; } = PackColor(0.666f, 0.000f, 0.000f, 1.000f);

    /// <summary>Color of the Y axis handle.</summary>
    public uint DirectionYColor { get; set; } = PackColor(0.000f, 0.666f, 0.000f, 1.000f);

    /// <summary>Color of the Z axis handle.</summary>
    public uint DirectionZColor { get; set; } = PackColor(0.000f, 0.000f, 0.666f, 1.000f);

    /// <summary>Color of the YZ plane handle (normal X).</summary>
    public uint PlaneXColor { get; set; } = PackColor(0.666f, 0.000f, 0.000f, 0.380f);

    /// <summary>Color of the XZ plane handle (normal Y).</summary>
    public uint PlaneYColor { get; set; } = PackColor(0.000f, 0.666f, 0.000f, 0.380f);

    /// <summary>Color of the XY plane handle (normal Z).</summary>
    public uint PlaneZColor { get; set; } = PackColor(0.000f, 0.000f, 0.666f, 0.380f);

    /// <summary>Color of the hovered or active handle.</summary>
    public uint SelectionColor { get; set; } = PackColor(1.000f, 0.500f, 0.062f, 0.541f);

    /// <summary>Color of the drag guide line shown while translating.</summary>
    public uint TranslationLineColor { get; set; } = PackColor(0.666f, 0.666f, 0.666f, 0.666f);

    /// <summary>Color of the reference line shown while scaling.</summary>
    public uint ScaleLineColor { get; set; } = PackColor(0.250f, 0.250f, 0.250f, 1.000f);

    /// <summary>Border color of the angle sector shown while rotating.</summary>
    public uint RotationUsingBorderColor { get; set; } = PackColor(1.000f, 0.500f, 0.062f, 1.000f);

    /// <summary>Fill color of the angle sector shown while rotating.</summary>
    public uint RotationUsingFillColor { get; set; } = PackColor(1.000f, 0.500f, 0.062f, 0.500f);

    /// <summary>Color of the hatched lines drawn on flipped axes.</summary>
    public uint HatchedAxisLinesColor { get; set; } = PackColor(0.000f, 0.000f, 0.000f, 0.500f);

    /// <summary>Color of the drag info text.</summary>
    public uint TextColor { get; set; } = PackColor(1.000f, 1.000f, 1.000f, 1.000f);

    /// <summary>Color of the drag info text shadow.</summary>
    public uint TextShadowColor { get; set; } = PackColor(0.000f, 0.000f, 0.000f, 1.000f);

    /// <summary>
    /// Packs floating point RGBA components into an ImU32 color
    /// (R | G &lt;&lt; 8 | B &lt;&lt; 16 | A &lt;&lt; 24), matching ImGui's ColorConvertFloat4ToU32.
    /// </summary>
    /// <param name="r">Red component, clamped to [0, 1].</param>
    /// <param name="g">Green component, clamped to [0, 1].</param>
    /// <param name="b">Blue component, clamped to [0, 1].</param>
    /// <param name="a">Alpha component, clamped to [0, 1].</param>
    /// <returns>The packed color.</returns>
    public static uint PackColor(float r, float g, float b, float a)
    {
        uint ri = (uint)(Math.Clamp(r, 0f, 1f) * 255f + 0.5f);
        uint gi = (uint)(Math.Clamp(g, 0f, 1f) * 255f + 0.5f);
        uint bi = (uint)(Math.Clamp(b, 0f, 1f) * 255f + 0.5f);
        uint ai = (uint)(Math.Clamp(a, 0f, 1f) * 255f + 0.5f);
        return ri | (gi << 8) | (bi << 16) | (ai << 24);
    }
}
