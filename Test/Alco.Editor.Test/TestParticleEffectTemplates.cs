using System.Text.Json;
using Alco.Engine;
using Alco.Particles;
using NUnit.Framework;

namespace Alco.Editor.Test;

/// <summary>
/// Tests for <see cref="ParticleEffectTemplates"/>: the "new asset" templates must
/// parse through the particle loader's own JSON options (self-contained — no texture
/// or material references) and survive a serialize → deserialize roundtrip, which is
/// the document's preview-rebuild path.
/// </summary>
[TestFixture]
public sealed class TestParticleEffectTemplates
{
    /// <summary>The minimal engine host for the asset/shader systems.</summary>
    private sealed class EngineHost() : GameEngine(GameEngineSetting.CreateNoGPU());

    [Test]
    public void Template2DParses()
    {
        using EngineHost engine = new();
        JsonSerializerOptions options = AssetLoaderParticleEffect.CreateJsonOptions(
            engine.AssetSystem, engine.RenderingSystem.ShaderSystem);

        ParticleEffectAsset asset = JsonSerializer.Deserialize<ParticleEffectAsset>(
            ParticleEffectTemplates.Effect2D, options)!;

        ParticleEffect2DAsset effect = asset as ParticleEffect2DAsset
            ?? throw new AssertionException("the 2D template is not a 2D effect");
        Assert.That(effect.Version, Is.EqualTo(ParticleEffectAsset.FormatVersion));
        Assert.That(effect.Groups, Has.Count.EqualTo(1));
        Assert.That(effect.Groups[0].Texture, Is.Null, "templates must stay self-contained");
        Assert.That(effect.Groups[0].Material, Is.Null, "templates must stay self-contained");
        Assert.That(effect.Groups[0].Behavior, Is.Null, "templates must stay self-contained");
    }

    [Test]
    public void Template3DParses()
    {
        using EngineHost engine = new();
        JsonSerializerOptions options = AssetLoaderParticleEffect.CreateJsonOptions(
            engine.AssetSystem, engine.RenderingSystem.ShaderSystem);

        ParticleEffectAsset asset = JsonSerializer.Deserialize<ParticleEffectAsset>(
            ParticleEffectTemplates.Effect3D, options)!;

        ParticleEffect3DAsset effect = asset as ParticleEffect3DAsset
            ?? throw new AssertionException("the 3D template is not a 3D effect");
        Assert.That(effect.Version, Is.EqualTo(ParticleEffectAsset.FormatVersion));
        Assert.That(effect.Groups, Has.Count.EqualTo(1));
    }

    [Test]
    public void TemplateSurvivesSerializeRoundtrip()
    {
        using EngineHost engine = new();
        JsonSerializerOptions options = AssetLoaderParticleEffect.CreateJsonOptions(
            engine.AssetSystem, engine.RenderingSystem.ShaderSystem);

        ParticleEffectAsset asset = JsonSerializer.Deserialize<ParticleEffectAsset>(
            ParticleEffectTemplates.Effect2D, options)!;

        string json = JsonSerializer.Serialize(asset, options);
        Assert.That(json, Does.Contain("$type"), "the polymorphic discriminator must be written");

        ParticleEffectAsset copy = JsonSerializer.Deserialize<ParticleEffectAsset>(json, options)!;
        ParticleEffect2DAsset effect = (ParticleEffect2DAsset)copy;
        ParticleGroup2DAsset group = effect.Groups[0];
        ParticleGroup2DAsset original = ((ParticleEffect2DAsset)asset).Groups[0];
        Assert.Multiple(() =>
        {
            Assert.That(group.EmissionRate, Is.EqualTo(original.EmissionRate));
            Assert.That(group.Lifetime.Min, Is.EqualTo(original.Lifetime.Min));
            Assert.That(group.Speed.Max, Is.EqualTo(original.Speed.Max));
            Assert.That(group.Shape.Type, Is.EqualTo(original.Shape.Type));
            Assert.That(group.Blend, Is.EqualTo(original.Blend), "blend presets must roundtrip by name");
        });
    }
}
