namespace Alco.Rendering;

// compute material factory

public partial class RenderingSystem
{
    /// <summary>
    /// Creates a new compute dispatcher whose variant starts at the given
    /// specialization — <see cref="ComputeMaterial.SetSpecializations"/> may
    /// switch it later.
    /// </summary>
    public ComputeMaterial CreateComputeMaterial(Shader shader, params ReadOnlySpan<object> specializations)
        => CreateComputeMaterial(shader, Shader.NormalizeSpecializations(specializations));

    /// <summary>Canonical-string core the compile paths use.</summary>
    internal ComputeMaterial CreateComputeMaterial(Shader shader, string[] specializations)
        => new(this, shader, specializations);
}
