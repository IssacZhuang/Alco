namespace Alco.Rendering;

// vector graphics processor factory (SVG rasterization)

public partial class RenderingSystem
{
    /// <summary>
    /// Creates an SVG rasterizer from the SvgRasterize compute shader: parsing,
    /// curve flattening and stroke expansion run on the CPU, coverage evaluation
    /// and compositing run on the GPU.
    /// </summary>
    /// <param name="shader">The SvgRasterize shader.</param>
    /// <returns>A new SvgRenderer instance.</returns>
    public SvgRenderer CreateSvgRenderer(Shader shader)
    {
        return new SvgRenderer(this, shader);
    }

    /// <summary>
    /// Creates an SVG rasterizer from the built-in SvgRasterize compute shader.
    /// </summary>
    /// <returns>A new SvgRenderer instance.</returns>
    public SvgRenderer CreateSvgRenderer()
    {
        return new SvgRenderer(this, ShaderSystem.GetShader("SvgRasterize"));
    }
}
