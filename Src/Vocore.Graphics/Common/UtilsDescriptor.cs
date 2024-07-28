namespace Vocore.Graphics;

public static class UtilsDescriptor
{
    public static bool IsGraphicsShader(this ShaderModule[] stages, out ShaderModule vertex, out ShaderModule pixel)
    {
        vertex = ShaderModule.Empty;
        pixel = ShaderModule.Empty;
        for (int i = 0; i < stages.Length; i++)
        {
            if (stages[i].Stage.HasFlag(ShaderStage.Vertex))
            {
                vertex = stages[i];
            }

            if (stages[i].Stage.HasFlag(ShaderStage.Fragment))
            {
                pixel = stages[i];
            }
        }
    
        return vertex.Stage != ShaderStage.None && pixel.Stage != ShaderStage.None;
    }

    public static bool IsComputeShader(this ShaderModule[] stages, out ShaderModule compute)
    {
        compute = ShaderModule.Empty;
        for (int i = 0; i < stages.Length; i++)
        {
            if (stages[i].Stage.HasFlag(ShaderStage.Compute))
            {
                compute = stages[i];
            }
        }

        return compute.Stage != ShaderStage.None;
    }
}