using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Alco.Graphics;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.GUI.Test;

/// <summary>Verifies injected canvas material libraries without a game asset system or GPU.</summary>
[TestFixture]
public sealed class CanvasMaterialTests
{
    private sealed class RenderingHost : IRenderingSystemHost, IDisposable
    {
        /// <summary>Notifies the rendering system that its owner is disposing.</summary>
        public event Action OnDispose = delegate { };
        /// <summary>Accepts update subscriptions without scheduling frames.</summary>
        public event Action<float> OnUpdate { add { } remove { } }
        /// <summary>Releases rendering-owned resources.</summary>
        public void Dispose() => OnDispose();
    }

    /// <summary>Composes ordinary and asset-based UI materials from explicitly resolved libraries.</summary>
    [Test]
    public void InjectedMaterials_WorkWithoutGameSetup()
    {
        GPUDevice device = GraphicsDeviceFactory.GetNoGPUDevice();
        using RenderingHost host = new();
        RenderingSystem rendering = new(host, device, PixelFormat.RGBA16Float, PixelFormat.Depth24PlusStencil8);
        ShaderLibrary spriteTemplate = rendering.ShaderSystem.GetLibrary("AlcoRendering_UISprite");
        ShaderLibrary textTemplate = rendering.ShaderSystem.GetLibrary("AlcoRendering_UIText");
        ShaderLibrary defaultSurface = rendering.ShaderSystem.GetLibrary("AlcoRendering_UIStandard");
        using Font font = rendering.CreateFont(rendering.TextureWhite, new GlyphInfo[128]);
        using Canvas first = rendering.CreateCanvas(new FakeInputTracker(), spriteTemplate, textTemplate, defaultSurface, font);
        using Canvas second = rendering.CreateCanvas(new FakeInputTracker(), spriteTemplate, textTemplate, defaultSurface, font);
        MaterialAsset asset = new() { Name = "DefaultUI" };

        GraphicsMaterial sprite = first.GetSpriteMaterial(asset, BlendState.AlphaBlend);
        GraphicsMaterial text = first.GetTextMaterial(asset);
        GraphicsMaterial otherCanvasSprite = second.GetSpriteMaterial(asset, BlendState.AlphaBlend);
        GraphicsMaterial otherCanvasText = second.GetTextMaterial(asset);
        GraphicsMaterial defaultSprite = GetDefaultSpriteMaterial(first);
        GraphicsMaterial defaultText = GetDefaultTextMaterial(first);
        GraphicsMaterial secondDefaultSprite = GetDefaultSpriteMaterial(second);
        GraphicsMaterial secondDefaultText = GetDefaultTextMaterial(second);

        Assert.Multiple(() =>
        {
            Assert.That(sprite.Shader, Is.SameAs(rendering.MaterialCompiler.ComposeGraphics(spriteTemplate, defaultSurface)));
            Assert.That(text.Shader, Is.SameAs(rendering.MaterialCompiler.ComposeGraphics(textTemplate, defaultSurface)));
            Assert.That(defaultSprite.Shader, Is.SameAs(sprite.Shader), "Ordinary sprites must use the injected composition.");
            Assert.That(defaultText.Shader, Is.SameAs(text.Shader), "Ordinary glyphs must use the injected composition.");
            Assert.That(sprite.Shader, Is.Not.SameAs(text.Shader));
            Assert.That(defaultSprite.BlendState, Is.EqualTo(BlendState.AlphaBlend));
            Assert.That(defaultText.BlendState, Is.EqualTo(BlendState.PremultipliedAlpha));
            Assert.That(defaultSprite.Specializations, Is.EqualTo(new[] { "false" }), "UI nearest defaults to false.");
            Assert.That(defaultText.Specializations, Is.EqualTo(new[] { "false" }), "UI nearest defaults to false.");
            Assert.That(sprite.Specializations, Is.EqualTo(new[] { "false" }));
            Assert.That(text.Specializations, Is.EqualTo(new[] { "false" }));
            Assert.That(first.GetSpriteMaterial(asset, BlendState.AlphaBlend), Is.SameAs(sprite));
            Assert.That(first.GetTextMaterial(asset), Is.SameAs(text));
            Assert.That(text.BlendState, Is.EqualTo(BlendState.PremultipliedAlpha));
            Assert.That(first.GetSpriteMaterial(asset, BlendState.PremultipliedAlpha), Is.Not.SameAs(sprite));
            Assert.That(otherCanvasSprite, Is.Not.SameAs(sprite));
            Assert.That(otherCanvasSprite.Shader, Is.SameAs(sprite.Shader));
            Assert.That(otherCanvasText, Is.Not.SameAs(text));
            Assert.That(otherCanvasText.Shader, Is.SameAs(text.Shader));
            Assert.That(secondDefaultSprite, Is.Not.SameAs(defaultSprite));
            Assert.That(secondDefaultSprite.Shader, Is.SameAs(defaultSprite.Shader));
            Assert.That(secondDefaultText, Is.Not.SameAs(defaultText));
            Assert.That(secondDefaultText.Shader, Is.SameAs(defaultText.Shader));
            Assert.That(GetEffectiveTexture(defaultSprite, ShaderResourceId.Texture), Is.SameAs(rendering.TextureWhite));
            Assert.That(asset.Surface, Is.Null, "Selecting the UI default must not mutate the asset.");
            Assert.That(asset.Textures, Is.Empty);
            Assert.That(asset.Parameters, Is.Empty);
            Assert.That(asset.Specializations, Is.Empty);
            Assert.Throws<InvalidDataException>(() => rendering.MaterialCompiler.SurfaceOf(asset),
                "Canvas defaults must not configure the rendering-owned compiler.");
        });

        defaultSprite.SetTexture(ShaderResourceId.Texture, rendering.TextureBlack);
        Assert.That(GetEffectiveTexture(secondDefaultSprite, ShaderResourceId.Texture), Is.SameAs(rendering.TextureWhite),
            "Ordinary drawing bindings must remain canvas-local.");

        sprite.Dispose();
        GraphicsMaterial recreated = first.GetSpriteMaterial(asset, BlendState.AlphaBlend);
        Assert.Multiple(() =>
        {
            Assert.That(recreated, Is.Not.SameAs(sprite));
            Assert.That(recreated.Shader, Is.SameAs(sprite.Shader));
        });
    }

    /// <summary>Uses replacement templates and fallback surfaces without looking up built-in UI modules.</summary>
    [Test]
    public void InjectedLibraries_ReplaceTemplatesAndDefaultSurface()
    {
        const string spriteModule = "CanvasTest_Sprite";
        const string textModule = "CanvasTest_Text";
        const string surfaceModule = "CanvasTest_Surface";
        const string alternateSurfaceModule = "CanvasTest_AlternateSurface";
        Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AlcoRendering_Core.slang"] = ReadDeployedModule("AlcoRendering_Core"),
            ["AlcoRendering_UI.slang"] = ReadDeployedModule("AlcoRendering_UI"),
            [$"{spriteModule}.slang"] = ReadDeployedModule("AlcoRendering_UISprite")
                .Replace("AlcoRendering_UISprite", spriteModule),
            [$"{textModule}.slang"] = ReadDeployedModule("AlcoRendering_UIText")
                .Replace("AlcoRendering_UIText", textModule),
            [$"{surfaceModule}.slang"] = ReadDeployedModule("AlcoRendering_UIStandard")
                .Replace("AlcoRendering_UIStandard", surfaceModule),
            [$"{alternateSurfaceModule}.slang"] = ReadDeployedModule("AlcoRendering_UIStandard")
                .Replace("AlcoRendering_UIStandard", alternateSurfaceModule)
                .Replace("public Texture2D texture;", "public Texture2D texture; public Texture2D injectedTexture;")
                .Replace("return material.texture.Sample(input.mainSampler, input.uv) * input.tint;",
                    "return material.texture.Sample(input.mainSampler, input.uv) * material.injectedTexture.Sample(input.mainSampler, input.uv) * input.tint;"),
        };
        SlangFileResolver resolver = ShaderModuleResolver.Create(
            path => files.TryGetValue(path, out string? source) ? new MemoryStream(Encoding.UTF8.GetBytes(source)) : null,
            () => files.Keys);
        GPUDevice device = GraphicsDeviceFactory.GetNoGPUDevice();
        using RenderingHost host = new();
        RenderingSystem rendering = new(host, device, PixelFormat.RGBA16Float, PixelFormat.Depth24PlusStencil8, resolver);
        ShaderSystem shaders = rendering.ShaderSystem;
        ShaderLibrary spriteTemplate = shaders.GetLibrary(spriteModule);
        ShaderLibrary textTemplate = shaders.GetLibrary(textModule);
        ShaderLibrary defaultSurface = shaders.GetLibrary(surfaceModule);
        ShaderLibrary alternateSurface = shaders.GetLibrary(alternateSurfaceModule);
        using Font font = rendering.CreateFont(rendering.TextureWhite, new GlyphInfo[128]);
        using Canvas first = rendering.CreateCanvas(new FakeInputTracker(), spriteTemplate, textTemplate, defaultSurface, font);
        using Canvas second = new(rendering, new FakeInputTracker(), spriteTemplate, textTemplate, alternateSurface, font);
        MaterialAsset asset = new() { Name = "InjectedDefaultUI" };
        GraphicsMaterial sprite = first.GetSpriteMaterial(asset, BlendState.AlphaBlend);
        GraphicsMaterial text = first.GetTextMaterial(asset);
        GraphicsMaterial alternateSprite = second.GetSpriteMaterial(asset, BlendState.AlphaBlend);
        GraphicsMaterial alternateText = second.GetTextMaterial(asset);
        GraphicsMaterial defaultSprite = GetDefaultSpriteMaterial(second);
        GraphicsMaterial defaultText = GetDefaultTextMaterial(second);
        MaterialAsset explicitAsset = new() { Name = "ExplicitUI", Surface = defaultSurface };

        Assert.Multiple(() =>
        {
            Assert.That(sprite.Shader, Is.SameAs(rendering.MaterialCompiler.ComposeGraphics(spriteTemplate, defaultSurface)));
            Assert.That(text.Shader, Is.SameAs(rendering.MaterialCompiler.ComposeGraphics(textTemplate, defaultSurface)));
            Assert.That(GetDefaultSpriteMaterial(first).Shader, Is.SameAs(sprite.Shader));
            Assert.That(GetDefaultTextMaterial(first).Shader, Is.SameAs(text.Shader));
            Assert.That(alternateSprite.Shader, Is.SameAs(rendering.MaterialCompiler.ComposeGraphics(spriteTemplate, alternateSurface)));
            Assert.That(alternateText.Shader, Is.SameAs(rendering.MaterialCompiler.ComposeGraphics(textTemplate, alternateSurface)));
            Assert.That(defaultSprite.Shader, Is.SameAs(alternateSprite.Shader));
            Assert.That(defaultText.Shader, Is.SameAs(alternateText.Shader));
            Assert.That(alternateSprite.Shader, Is.Not.SameAs(sprite.Shader));
            Assert.That(alternateText.Shader, Is.Not.SameAs(text.Shader));
            Assert.That(sprite.TryGetResourceId("injectedTexture", out _), Is.False);
            Assert.That(text.TryGetResourceId("injectedTexture", out _), Is.False);
            Assert.That(defaultSprite.TryGetResourceId("injectedTexture", out _), Is.True,
                "The injected surface must change the ordinary sprite's linked resource layout.");
            Assert.That(defaultText.TryGetResourceId("injectedTexture", out _), Is.True,
                "The injected surface must change the ordinary text's linked resource layout.");
            Assert.That(GetEffectiveTexture(defaultSprite, "injectedTexture"), Is.SameAs(rendering.TextureWhite));
            Assert.That(GetEffectiveTexture(defaultText, "injectedTexture"), Is.SameAs(rendering.TextureWhite));
            Assert.That(defaultSprite.Specializations, Is.EqualTo(new[] { "false" }));
            Assert.That(defaultText.Specializations, Is.EqualTo(new[] { "false" }));
            Assert.That(alternateSprite.Specializations, Is.EqualTo(new[] { "false" }));
            Assert.That(alternateText.Specializations, Is.EqualTo(new[] { "false" }));
            Assert.That(second.GetSpriteMaterial(explicitAsset, BlendState.AlphaBlend).Shader, Is.SameAs(sprite.Shader),
                "An authored surface must take precedence over the injected fallback.");
            Assert.That(second.GetTextMaterial(explicitAsset).Shader, Is.SameAs(text.Shader));
            Assert.That(explicitAsset.Surface, Is.SameAs(defaultSurface));
            Assert.That(asset.Surface, Is.Null);
            Assert.That(asset.Textures, Is.Empty);
            Assert.That(asset.Parameters, Is.Empty);
            Assert.That(asset.Specializations, Is.Empty);
            Assert.Throws<InvalidDataException>(() => rendering.MaterialCompiler.SurfaceOf(asset));
            Assert.That(shaders.Modules.GetModuleSource("AlcoRendering_UISprite"), Is.Null);
            Assert.That(shaders.Modules.GetModuleSource("AlcoRendering_UIText"), Is.Null);
            Assert.That(shaders.Modules.GetModuleSource("AlcoRendering_UIStandard"), Is.Null);
        });
    }

    /// <summary>The canvas cache must not keep otherwise unused material assets alive.</summary>
    [Test]
    public void MaterialCache_DoesNotRetainUnusedAssets()
    {
        GPUDevice device = GraphicsDeviceFactory.GetNoGPUDevice();
        using RenderingHost host = new();
        RenderingSystem rendering = new(host, device, PixelFormat.RGBA16Float, PixelFormat.Depth24PlusStencil8);
        ShaderLibrary spriteTemplate = rendering.ShaderSystem.GetLibrary("AlcoRendering_UISprite");
        ShaderLibrary textTemplate = rendering.ShaderSystem.GetLibrary("AlcoRendering_UIText");
        ShaderLibrary defaultSurface = rendering.ShaderSystem.GetLibrary("AlcoRendering_UIStandard");
        using Font font = rendering.CreateFont(rendering.TextureWhite, new GlyphInfo[128]);
        using Canvas canvas = rendering.CreateCanvas(new FakeInputTracker(), spriteTemplate, textTemplate, defaultSurface, font);
        WeakReference<MaterialAsset> asset = CacheTemporaryAsset(canvas);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(asset.TryGetTarget(out _), Is.False);
        GC.KeepAlive(canvas);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<MaterialAsset> CacheTemporaryAsset(Canvas canvas)
    {
        MaterialAsset asset = new() { Name = "TemporaryUI" };
        canvas.GetSpriteMaterial(asset, BlendState.AlphaBlend);
        return new WeakReference<MaterialAsset>(asset);
    }

    /// <summary>Keeps node overrides isolated and recreates instances when moved to another canvas.</summary>
    [Test]
    public void NodeInstances_AreIsolatedAndFollowTheirCanvas()
    {
        GPUDevice device = GraphicsDeviceFactory.GetNoGPUDevice();
        using RenderingHost host = new();
        RenderingSystem rendering = new(host, device, PixelFormat.RGBA16Float, PixelFormat.Depth24PlusStencil8);
        ShaderLibrary spriteTemplate = rendering.ShaderSystem.GetLibrary("AlcoRendering_UISprite");
        ShaderLibrary textTemplate = rendering.ShaderSystem.GetLibrary("AlcoRendering_UIText");
        ShaderLibrary defaultSurface = rendering.ShaderSystem.GetLibrary("AlcoRendering_UIStandard");
        using Font font = rendering.CreateFont(rendering.TextureWhite, new GlyphInfo[128]);
        using Canvas first = rendering.CreateCanvas(new FakeInputTracker(), spriteTemplate, textTemplate, defaultSurface, font);
        using Canvas second = rendering.CreateCanvas(new FakeInputTracker(), spriteTemplate, textTemplate, defaultSurface, font);
        MaterialAsset asset = new() { Name = "SharedUI" };
        UIImage image = new() { Material = asset, Size = new Vector2(100f) };
        UIImage other = new() { Material = asset, Size = new Vector2(100f) };
        UIText text = new() { Material = asset, Font = font, Size = new Vector2(100f) };
        UIGeometryIcon icon = new() { Material = asset, Size = new Vector2(100f) };
        first.Root.Add(image);
        image.Texture = rendering.TextureWhite;
        first.Root.Add(other);
        first.Root.Add(text);
        first.Root.Add(icon);

        GraphicsMaterialInstance imageInstance = image.GetMaterialInstance(first);
        GraphicsMaterialInstance otherInstance = other.GetMaterialInstance(first);
        GraphicsMaterialInstance textInstance = text.GetMaterialInstance(first);
        GraphicsMaterialInstance iconInstance = icon.GetMaterialInstance(first);
        Assert.Multiple(() =>
        {
            Assert.That(image.GetMaterialInstance(first), Is.SameAs(imageInstance));
            Assert.That(otherInstance, Is.Not.SameAs(imageInstance));
            Assert.That(text.GetMaterialInstance(first), Is.SameAs(textInstance));
            Assert.That(icon.GetMaterialInstance(first), Is.SameAs(iconInstance));
            Assert.That(iconInstance.BlendState, Is.EqualTo(BlendState.PremultipliedAlpha));
        });

        ShaderParameterSet imageParameters = GetParameters(imageInstance);
        Assert.That(imageParameters.GetTexture(ShaderResourceId.Texture), Is.SameAs(rendering.TextureWhite));
        image.Texture = null;
        Assert.That(image.GetMaterialInstance(first), Is.SameAs(imageInstance));
        Assert.That(imageParameters.GetTexture(ShaderResourceId.Texture), Is.Null);
        using GraphicsMaterialInstance fresh = first.CreateUIMaterialInstance(first.GetSpriteMaterial(asset, BlendState.AlphaBlend));
        uint textureId = imageInstance.GetResourceId(ShaderResourceId.Texture);
        int textureGroup = imageInstance.ReflectionInfo.ResourceLocations[(int)textureId].GroupIndex;
        Assert.That(imageInstance[textureGroup], Is.Not.Null);
        Assert.That(fresh[textureGroup], Is.Not.Null);

        first.Root.Remove(image);
        Assert.That(imageInstance.IsDisposed, Is.True);
        second.Root.Add(image);
        using GraphicsMaterialInstance moved = image.GetMaterialInstance(second);
        using GraphicsMaterialInstance cameraReference = second.CreateUIMaterialInstance(second.GetSpriteMaterial(asset, BlendState.AlphaBlend));
        ShaderParameterSet movedParameters = GetParameters(moved);
        ShaderParameterSet referenceParameters = GetParameters(cameraReference);
        Assert.That(movedParameters.TryGetBuffer(ShaderResourceId.Camera, out GraphicsBuffer? movedCamera), Is.True);
        Assert.That(referenceParameters.TryGetBuffer(ShaderResourceId.Camera, out GraphicsBuffer? expectedCamera), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.Not.SameAs(imageInstance));
            Assert.That(movedCamera, Is.SameAs(expectedCamera));
        });

        other.Material = null;
        Assert.That(otherInstance.IsDisposed, Is.True);
        Assert.Throws<InvalidOperationException>(() => other.GetMaterialInstance(first));
        text.Material = null;
        icon.Material = null;
        Assert.Multiple(() =>
        {
            Assert.That(textInstance.IsDisposed, Is.True);
            Assert.That(iconInstance.IsDisposed, Is.True);
        });

        UIInputBox input = new() { Size = new Vector2(100f) };
        second.Root.Add(input);
        second.SetTextInputArea(input, input.Bound, 0);
        Assert.That(second.IsCapturingKeyboard, Is.True);
        second.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(moved.IsDisposed, Is.True);
            Assert.That(image.Parent, Is.Null);
            Assert.That(second.IsCapturingKeyboard, Is.False);
            Assert.That(((FakeInputTracker)second.InputTracker).TextInputRequestCount, Is.EqualTo(1));
            Assert.That(((FakeInputTracker)second.InputTracker).TextInputReleaseCount, Is.EqualTo(1));
        });
    }

    private static string ReadDeployedModule(string module)
    {
        string shaders = Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders");
        string path = Directory.EnumerateFiles(shaders, $"{module}.slang", SearchOption.AllDirectories).Single();
        return File.ReadAllText(path);
    }

    private static GraphicsMaterial GetDefaultSpriteMaterial(Canvas canvas)
    {
        SpriteRenderer renderer = GetPrivateField<SpriteRenderer>(canvas, "_spriteRenderer");
        return GetPrivateField<GraphicsMaterial>(renderer, "_material");
    }

    private static GraphicsMaterial GetDefaultTextMaterial(Canvas canvas)
    {
        TextRenderer renderer = GetPrivateField<TextRenderer>(canvas, "_textRenderer");
        return GetPrivateField<GraphicsMaterial>(renderer, "_material");
    }

    private static T GetPrivateField<T>(object owner, string name)
    {
        return (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    }

    private static Texture2D? GetEffectiveTexture(GraphicsMaterial material, string name)
    {
        ShaderParameterSet? parameters = GetParameters(material);
        while (parameters != null)
        {
            if (parameters.TryGetTexture(name, out Texture2D? texture))
            {
                return texture;
            }
            parameters = (ShaderParameterSet?)typeof(ShaderParameterSet)
                .GetProperty("Fallback", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(parameters);
        }
        return null;
    }

    private static ShaderParameterSet GetParameters(GraphicsMaterial material)
    {
        return (ShaderParameterSet)typeof(GraphicsMaterial)
            .GetProperty("Parameters", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(material)!;
    }
}

