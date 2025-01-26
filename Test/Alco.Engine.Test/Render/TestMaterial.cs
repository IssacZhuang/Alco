using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine.Test;

public class TestMaterial
{
    [Test]
    public void TestMaterialInheritance()
    {
        GameEngine engine = new GameEngine(GameEngineSetting.CreateNoGPU());
        RenderingSystem renderingSystem = engine.Rendering;
        Shader shader = engine.BuiltInAssets.Shader_Sprite;
        GraphicsMaterial material = renderingSystem.CreateGraphicsMaterial(shader, "root");
        GraphicsBuffer camera = renderingSystem.CreateCamera2D(1280, 720, 1000);

        material.SetBuffer(0, camera);

        MaterialInstance instance1 = material.CreateInstance();

        Assert.IsTrue(instance1[0] == camera.EntryReadonly);

        instance1.SetTexture(1, renderingSystem.TextureWhite);

        MaterialInstance instance2 = instance1.CreateInstance();
        MaterialInstance instance3 = instance1.CreateInstance();

        instance2.SetTexture(1, renderingSystem.TextureBlack);

        Assert.IsTrue(instance2[1] == renderingSystem.TextureBlack.EntrySample);
        //use parent resource if not set
        Assert.IsTrue(instance3[1] == renderingSystem.TextureWhite.EntrySample);

        Assert.IsTrue(instance2[0] == camera.EntryReadonly);
        Assert.IsTrue(instance3[0] == camera.EntryReadonly);

        

    }
}