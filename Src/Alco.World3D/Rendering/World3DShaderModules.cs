namespace Alco.World3D;

/// <summary>
/// Load names of the entry-point slang modules shipped with the Alco.World3D
/// module (the dashed file stem, per docs/SlangCodingStandard.md) that are
/// still referenced from code. The pass modules consumed by render nodes moved
/// into <c>.rnfact</c> factory assets (see <c>Assets/RenderNodes</c>): their
/// module names live in that data now, so retargeting a node's shaders is a
/// config edit. Mount a file source serving this module's <c>Assets</c> folder
/// first (the module's content is copied into the application's output
/// <c>Assets</c> folder automatically when it is referenced); the module system
/// resolves each name to its source file.
/// </summary>
public static class World3DShaderModules
{
    /// <summary>The asset folder the module's shader files live under (used by tests to enumerate them).</summary>
    public const string Folder = "Shaders/Pipelines/Rendering/PBR/";
}
