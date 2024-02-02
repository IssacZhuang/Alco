namespace Vocore.Graphics;

public static class UtilsDescriptor
{
    public static bool IsGraphicsShader(this ShaderStageSource[] stages, out ShaderStageSource vertex, out ShaderStageSource pixel)
    {
        vertex = ShaderStageSource.Empty;
        pixel = ShaderStageSource.Empty;
        for (int i = 0; i < stages.Length; i++)
        {
            if (stages[i].Stage.HasFlag(ShaderStage.Vertex))
            {
                vertex = stages[i];
            }

            if (stages[i].Stage.HasFlag(ShaderStage.Pixel))
            {
                pixel = stages[i];
            }
        }
    
        return vertex.Stage != ShaderStage.None && pixel.Stage != ShaderStage.None;
    }

    public static bool IsComputeShader(this ShaderStageSource[] stages, out ShaderStageSource compute)
    {
        compute = ShaderStageSource.Empty;
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