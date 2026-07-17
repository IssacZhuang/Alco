namespace Alco.ImGUI;

/// <summary>
/// Selects which manipulation handles a gizmo displays and responds to.
/// Flags can be combined freely; a plane translation handle appears automatically
/// when both axis bits of that plane are set (there is no dedicated plane bit).
/// </summary>
[Flags]
public enum GizmoOperation
{
    /// <summary>No operation.</summary>
    None = 0,

    /// <summary>Translate along the X axis.</summary>
    TranslateX = 1 << 0,

    /// <summary>Translate along the Y axis.</summary>
    TranslateY = 1 << 1,

    /// <summary>Translate along the Z axis.</summary>
    TranslateZ = 1 << 2,

    /// <summary>Rotate around the X axis.</summary>
    RotateX = 1 << 3,

    /// <summary>Rotate around the Y axis.</summary>
    RotateY = 1 << 4,

    /// <summary>Rotate around the Z axis.</summary>
    RotateZ = 1 << 5,

    /// <summary>Rotate around the camera view axis (screen-space ring).</summary>
    RotateScreen = 1 << 6,

    /// <summary>Scale along the X axis.</summary>
    ScaleX = 1 << 7,

    /// <summary>Scale along the Y axis.</summary>
    ScaleY = 1 << 8,

    /// <summary>Scale along the Z axis.</summary>
    ScaleZ = 1 << 9,

    /// <summary>Scale uniformly on all axes (center handle).</summary>
    ScaleUniform = 1 << 10,

    /// <summary>Translate in the XY plane (alias for the two axis bits).</summary>
    TranslateXY = TranslateX | TranslateY,

    /// <summary>Translate in the YZ plane (alias for the two axis bits).</summary>
    TranslateYZ = TranslateY | TranslateZ,

    /// <summary>Translate in the XZ plane (alias for the two axis bits).</summary>
    TranslateXZ = TranslateX | TranslateZ,

    /// <summary>Translate along all axes.</summary>
    Translate = TranslateX | TranslateY | TranslateZ,

    /// <summary>Rotate around all axes.</summary>
    Rotate = RotateX | RotateY | RotateZ,

    /// <summary>Scale along all axes.</summary>
    Scale = ScaleX | ScaleY | ScaleZ,
}
