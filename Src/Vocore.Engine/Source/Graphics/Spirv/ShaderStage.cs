namespace Vocore.Engine
{
    /// <summary>
    /// Represents the different stages of a shader.
    /// </summary>
    public enum ShaderStage
    {
        None = 0,
        Vertex = 1,
        /// <summary>
        /// Also known as the tessellation control shader.
        /// </summary>
        Hull = 2,
        /// <summary>
        /// Also known as the tessellation evaluation shader.
        /// </summary>
        Domain = 4,
        Geometry = 8,
        Pixel = 16,
        Compute = 32,
    }
}