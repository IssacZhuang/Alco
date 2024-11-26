namespace Vocore.Graphics;

public static class UtilsDescriptor
{
    public static bool IsGraphicsShader(this ShaderModule[] modules)
    {
        ShaderStage stages = ShaderStage.None;
        for (int i = 0; i < modules.Length; i++)
        {
            stages |= modules[i].Stage;
        }

        return stages.HasFlag(ShaderStage.Vertex) && stages.HasFlag(ShaderStage.Fragment);
    }

    public static void GetVertexAndPixelModules(this ShaderModule[] modules, out ShaderModule vertex, out ShaderModule pixel)
    {
        vertex = ShaderModule.Empty;
        pixel = ShaderModule.Empty;

        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i].Stage.HasFlag(ShaderStage.Vertex))
                vertex = modules[i];
            if (modules[i].Stage.HasFlag(ShaderStage.Fragment))
                pixel = modules[i];
        }

        if (vertex.Stage == ShaderStage.None || pixel.Stage == ShaderStage.None)
            throw new GraphicsException("The shader stages must contain a vertex and a pixel shader when creating a graphics pipeline");
    }
}