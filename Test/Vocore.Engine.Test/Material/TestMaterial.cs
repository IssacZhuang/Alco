using NUnit.Framework;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine.Test;

public class TestMaterial
{
    public void TestMaterialInheritance()
    {
        GameEngine engine = new GameEngine(GameEngineSetting.CreateNoGPU());
        RenderingSystem renderingSystem = engine.Rendering;
        Shader shader = engine.BuiltInAssets.Shader_Sprite;
        GraphicsMaterial material = renderingSystem.CreateGraphicsMaterial(shader, "root");
        GraphicsBuffer camera = renderingSystem.CreateCamera2D(1280, 720, 1000);

        material.SetBuffer("_camera", camera);

        MaterialInstance instance1 = material.CreateInstance();

        Assert.IsTrue(instance1.DebugGetBuffer("_camera") == camera);

        instance1.SetTexture("_texture", renderingSystem.TextureWhite);

        MaterialInstance instance2 = instance1.CreateInstance();
        MaterialInstance instance3 = instance2.CreateInstance();

        instance2.SetTexture("_texture", renderingSystem.TextureBlack);

        Assert.IsTrue(instance2.DebugGetTexture("_texture") == renderingSystem.TextureBlack);
        //use parent resource if not set
        Assert.IsTrue(instance3.DebugGetTexture("_texture") == renderingSystem.TextureWhite);

        Assert.IsTrue(instance2.DebugGetBuffer("_camera") == camera);
        Assert.IsTrue(instance3.DebugGetBuffer("_camera") == camera);

        

    }
}