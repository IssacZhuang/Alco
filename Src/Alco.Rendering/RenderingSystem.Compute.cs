namespace Alco.Rendering;

// compute material factory

public partial class RenderingSystem
{
    /// <summary>
    /// Creates a compute dispatcher construction-bound to (shader, specialization):
    /// it pins one variant for its lifetime — runtime variant switching means
    /// constructing another dispatcher.
    /// </summary>
    public ComputeMaterial CreateComputeMaterial(Shader shader, params ReadOnlySpan<string> specializations)
    {
        return new ComputeMaterial(this, shader, specializations.ToArray());
    }
}
