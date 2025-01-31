using System.Numerics;

namespace Alco.Rendering;

public partial class RenderingSystem
{ 
    public ComputeDispatcher CreateComputeDispatcher(Shader shader)
    {
        return new ComputeDispatcher(this, shader);
    }

    public ComputeDispatcher CreateComputeDispatcher(Shader shader, ReadOnlySpan<string> defines)
    {
        return new ComputeDispatcher(this, shader, defines);
    }



    

}