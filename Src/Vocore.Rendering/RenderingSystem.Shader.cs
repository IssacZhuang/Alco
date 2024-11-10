using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    public Shader CreateShader(string shaderText, string name)
    {
        return new Shader(this, shaderText, name);
    }
}