namespace Alco.Graphics
{
    /// <summary>
    /// Which triangle faces are discarded before rasterization.
    /// </summary>
    public enum CullMode
    {
        /// <summary>Cull nothing; both facing directions are drawn.</summary>
        None,
        /// <summary>Cull front-facing triangles.</summary>
        Front,
        /// <summary>Cull back-facing triangles.</summary>
        Back
    }
}