using Alco;
using Alco.Engine;
using Alco.Rendering;

public class GrassSystem : BaseEngineSystem
{
    private Material _material;

    public GrassSystem(GameEngine engine)
    {
        RenderingSystem renderingSystem = engine.Rendering;
        Shader shader = engine.BuiltInAssets.Shader_SpritePlantInstancing;
        _material = renderingSystem.CreateGraphicsMaterial(shader);
    }
}