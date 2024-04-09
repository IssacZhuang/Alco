namespace Vocore.Rendering;

public partial class RenderingSystem
{
    public U2ToneMap CreateU2ToneMap(Shader shader)
    {
        return new U2ToneMap(this, shader);
    }
}