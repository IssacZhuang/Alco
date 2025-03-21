using System.Numerics;

namespace Alco.Rendering;

// compute material factory

public partial class RenderingSystem
{
    public ComputeMaterial CreateComputeMaterial(Shader shader)
    {
        return new ComputeMaterial(this, shader);
    }

    public ComputeMaterial CreateComputeMaterial(Shader shader, ReadOnlySpan<string> defines)
    {
        return new ComputeMaterial(this, shader, defines);
    }



    

}