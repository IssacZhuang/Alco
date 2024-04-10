namespace Vocore.Rendering;

public partial class RenderingSystem
{
    public Uncharted2ToneMap CreateUncharted2ToneMap(Shader shader)
    {
        return new Uncharted2ToneMap(this, shader);
    }

    public ReinhardLuminanceToneMap CreateReinhardLuminanceToneMap(Shader shader)
    {
        return new ReinhardLuminanceToneMap(this, shader);
    }
}