namespace Vocore.Graphics;

[Flags]
public enum ShaderStage
{
    None = 0,
    Vertex = 1 << 0,
    // currently not supported
    Hull = 1 << 1,
    // currently not supported
    Domain = 1 << 2,
    // currently not supported
    Geometry = 1 << 3,
    Pixel = 1 << 4,
    Compute = 1 << 5,
}