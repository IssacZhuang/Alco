// Internal shader-stage flags for runtime Slang compilation.

namespace Alco.Graphics.Slang;

[Flags]
internal enum SlangShaderStage
{
    None = 0,
    Vertex = 1 << 0,
    Hull = 1 << 1,
    Domain = 1 << 2,
    Geometry = 1 << 3,
    Pixel = 1 << 4,
    Compute = 1 << 5,
    Mesh = 1 << 6,
    Standard = Vertex | Pixel | Compute,
    Task = 1 << 7,
}
