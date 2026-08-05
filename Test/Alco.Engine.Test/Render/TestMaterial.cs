using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine.Test;

public class TestMaterial
{
    [Test]
    public void TestMaterialInheritance()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        Shader shader = engine.BuiltInAssets.Shader_Sprite;
        GraphicsMaterial material = renderingSystem.CreateMaterial(shader, "root");
        GraphicsBuffer camera = renderingSystem.CreateCamera2D(1280, 720, 1000);

        material.SetBuffer(0, camera);

        MaterialInstance instance1 = material.CreateInstance();

        // Bind groups are assembled per material from the slot values; the instance
        // resolves unbound values from the parent chain, so its group is complete
        // even though it set nothing itself.
        Assert.IsTrue(instance1[0] != null);

        instance1.SetTexture(1, renderingSystem.TextureWhite);

        MaterialInstance instance2 = instance1.CreateInstance();
        MaterialInstance instance3 = instance1.CreateInstance();

        instance2.SetTexture(1, renderingSystem.TextureBlack);

        // The override (black) and the inherited value (white) assemble to
        // different groups.
        Assert.IsTrue(instance2[1] != null);
        Assert.IsTrue(instance3[1] != null);
        Assert.IsTrue(instance2[1] != instance3[1]);

        // Unbound slots fall back to the parent chain in every group.
        Assert.IsTrue(instance2[0] != null);
        Assert.IsTrue(instance3[0] != null);

        // Unchanged contents are served from the content cache: repeated access
        // returns the same group object instead of rebuilding it.
        Assert.AreSame(instance2[0], instance2[0]);
        Assert.AreSame(instance3[1], instance3[1]);
    }

    [Test]
    public void TestCameraBufferFlushedOnGroupAssembly()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        Shader shader = engine.BuiltInAssets.Shader_Sprite;
        GraphicsMaterial material = renderingSystem.CreateMaterial(shader, "root");
        Camera2DBuffer camera = renderingSystem.CreateCamera2D(1280, 720, 1000);
        camera.Position = new System.Numerics.Vector2(100, 100);

        // The camera has a pending matrix upload (dirty) which used to be flushed by
        // SetBuffer through the EntryReadonly getter. The bind group assembly must
        // preserve that flush, otherwise the bound camera UBO stays zero.
        Assert.IsTrue(IsDirty(camera));

        material.SetBuffer(0, camera);
        Assert.IsTrue(material[0] != null);
        Assert.IsFalse(IsDirty(camera));

        // The instance resolves the camera from the parent chain on every flush,
        // so a later matrix change is picked up without rebinding.
        MaterialInstance instance = material.CreateInstance();
        camera.Position = new System.Numerics.Vector2(200, 200);
        Assert.IsTrue(IsDirty(camera));
        Assert.IsTrue(instance[0] != null);
        Assert.IsFalse(IsDirty(camera));
    }

    private static bool IsDirty(Camera2DBuffer camera)
    {
        System.Reflection.FieldInfo? field = typeof(BaseCameraBuffer<CameraData2D>)
            .GetField("_dirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        return (bool)field!.GetValue(camera)!;
    }
}
