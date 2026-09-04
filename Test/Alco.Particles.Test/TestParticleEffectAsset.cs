using System.Runtime.InteropServices;
using System.Text.Json;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.Particles;

namespace Alco.Particles.Test;

/// <summary>
/// Particle effect asset (<c>.afx</c>) parsing tests and GPU-layout guards of
/// the C#/slang struct twins.
/// </summary>
public class TestParticleEffectAsset
{
    /// <summary>The minimal engine host for the asset/shader systems.</summary>
    public class EngineHost(GameEngineSetting setting) : GameEngine(setting);

    private const string Effect2DJson = """
        {
            // a comment, tolerated
            "$type": "Alco.Particles.ParticleEffect2DAsset",
            "version": "1.0",
            "groups": [
                {
                    "name": "Sparks",
                    "maxParticles": 2048,
                    "emissionRate": 500,
                    "looping": false,
                    "duration": 2.0,
                    "bursts": [ { "time": 0.0, "countMin": 40, "countMax": 60 } ],
                    "shape": { "type": "circle", "radius": 12.0, "innerRadius": 0.5 },
                    "direction": { "x": 0, "y": 1 },
                    "directionMode": "radial",
                    "spreadAngle": 0.5,
                    "speed": { "min": 100, "max": 200 },
                    "lifetime": { "min": 0.5, "max": 1.0 },
                    "size": { "min": { "x": 2, "y": 2 }, "max": 4 },
                    "startRotation": 0.0,
                    "angularVelocity": { "min": -3, "max": 3 },
                    "alignRotationToVelocity": true,
                    "gravity": { "x": 0, "y": -300 },
                    "startHeight": { "min": 0.25, "max": 0.75 },
                    "heightVelocity": { "min": 2, "max": 4 },
                    "heightAcceleration": -9.8,
                    "drag": 1.5,
                    "startColor": { "min": "#FFCC00FF", "max": "#FF5000FF" },
                    "endColor": "#00000000",
                    "colorGradient": [
                        { "time": 0.0, "color": "#FFFFFFFF" },
                        { "time": 0.5, "color": "#FF8800FF" },
                        { "time": 1.0, "color": "#00000000" }
                    ],
                    "sizeCurve": [
                        { "time": 0.0, "value": 0.5 },
                        { "time": 0.2, "value": 1.0 },
                        { "time": 1.0, "value": 0.0 }
                    ],
                    "velocityStretch": true,
                    "stretchLengthScale": 1.5,
                    "stretchSpeedScale": 0.08,
                    "fadeIn": 0.05,
                    "fadeOut": 0.4,
                    "endScale": 0.1,
                    "simulationSpace": "world",
                    "blend": "Additive",
                    "tint": { "x": 1, "y": 1, "z": 1, "w": 1 }
                },
                {
                    "name": "Smoke",
                    "maxParticles": 512
                }
            ]
        }
        """;

    [Test]
    public void ParsesEffect2DWithDefaultsAndStrictMembers()
    {
        using EngineHost engine = new(GameEngineSetting.CreateNoGPU());
        JsonSerializerOptions options = AssetLoaderParticleEffect.CreateJsonOptions(
            engine.AssetSystem, engine.RenderingSystem.ShaderSystem);

        ParticleEffectAsset asset = JsonSerializer.Deserialize<ParticleEffectAsset>(Effect2DJson, options)
            ?? throw new InvalidDataException("empty");

        Assert.Multiple(() =>
        {
            ParticleEffect2DAsset effect = asset as ParticleEffect2DAsset
                ?? throw new AssertionException("not a 2D effect");
            Assert.That(effect.Groups, Has.Count.EqualTo(2));

            ParticleGroup2DAsset sparks = effect.Groups[0];
            Assert.That(sparks.Name, Is.EqualTo("Sparks"));
            Assert.That(sparks.MaxParticles, Is.EqualTo(2048));
            Assert.That(sparks.EmissionRate, Is.EqualTo(500f));
            Assert.That(sparks.Looping, Is.False);
            Assert.That(sparks.Duration, Is.EqualTo(2f));
            Assert.That(sparks.Bursts, Has.Count.EqualTo(1));
            Assert.That(sparks.Bursts[0].CountMax, Is.EqualTo(60));
            Assert.That(sparks.Shape.Type, Is.EqualTo(ParticleShape2DType.Circle));
            Assert.That(sparks.Shape.InnerRadius, Is.EqualTo(0.5f));
            Assert.That(sparks.DirectionMode, Is.EqualTo(ParticleDirectionMode.Radial));
            Assert.That(sparks.Speed.Min, Is.EqualTo(100f));
            Assert.That(sparks.AlignRotationToVelocity, Is.True);
            Assert.That(sparks.Gravity.Y, Is.EqualTo(-300f));
            Assert.That(sparks.StartHeight.Min, Is.EqualTo(0.25f));
            Assert.That(sparks.StartHeight.Max, Is.EqualTo(0.75f));
            Assert.That(sparks.HeightVelocity.Min, Is.EqualTo(2f));
            Assert.That(sparks.HeightVelocity.Max, Is.EqualTo(4f));
            Assert.That(sparks.HeightAcceleration, Is.EqualTo(-9.8f));
            Assert.That(sparks.StartColor.Max.R, Is.EqualTo(1f));
            Assert.That(sparks.StartColor.Max.G, Is.EqualTo(0x50 / 255f).Within(1e-6));
            Assert.That(sparks.EndColor.A, Is.EqualTo(0f));
            Assert.That(sparks.ColorGradient, Has.Count.EqualTo(3));
            Assert.That(sparks.ColorGradient![1].Time, Is.EqualTo(0.5f));
            Assert.That(sparks.ColorGradient[1].Color.R, Is.EqualTo(1f));
            Assert.That(sparks.ColorGradient[1].Color.G, Is.EqualTo(0x88 / 255f).Within(1e-6));
            Assert.That(sparks.SizeCurve, Has.Count.EqualTo(3));
            Assert.That(sparks.SizeCurve![2].Value, Is.EqualTo(0f));
            Assert.That(sparks.VelocityStretch, Is.True);
            Assert.That(sparks.StretchLengthScale, Is.EqualTo(1.5f));
            Assert.That(sparks.StretchSpeedScale, Is.EqualTo(0.08f));
            Assert.That(sparks.SimulationSpace, Is.EqualTo(ParticleSimulationSpace.World));
            Assert.That(sparks.Blend, Is.Not.Null);
            Assert.That(sparks.Material, Is.Null);
            Assert.That(sparks.Behavior, Is.Null);

            // The second group takes every default.
            ParticleGroup2DAsset smoke = effect.Groups[1];
            Assert.That(smoke.Shape.Type, Is.EqualTo(ParticleShape2DType.Point));
            Assert.That(smoke.SimulationSpace, Is.EqualTo(ParticleSimulationSpace.World));
            Assert.That(smoke.Tint, Is.EqualTo(ColorFloat.White));
            Assert.That(smoke.ColorGradient, Is.Null);
            Assert.That(smoke.SizeCurve, Is.Null);
            Assert.That(smoke.VelocityStretch, Is.False);
            Assert.That(smoke.StretchLengthScale, Is.EqualTo(1f));
            Assert.That(smoke.StretchSpeedScale, Is.EqualTo(0.05f));
        });

        // The emitter params carry the over-life/stretch settings as flag bits
        // and spare lanes (the struct layouts stay at 336/320 bytes).
        EmitterParams2D parameters = EmitterParams2D.FromAsset(
            (asset as ParticleEffect2DAsset)!.Groups[0], 6);
        EmitterParams2D defaults = EmitterParams2D.FromAsset(
            (asset as ParticleEffect2DAsset)!.Groups[1], 6);
        Assert.Multiple(() =>
        {
            Assert.That(parameters.Flags & EmitterParams2D.FlagColorGradient, Is.Not.Zero);
            Assert.That(parameters.Flags & EmitterParams2D.FlagSizeCurve, Is.Not.Zero);
            Assert.That(parameters.Flags & EmitterParams2D.FlagVelocityStretch, Is.Not.Zero,
                "stretch + alignRotationToVelocity sets the stretch bit");
            Assert.That(parameters.Speed.W, Is.EqualTo(0.08f));
            Assert.That(parameters.OverLife.Y, Is.EqualTo(1.5f));
            Assert.That(parameters.HeightMotion, Is.EqualTo(new System.Numerics.Vector4(0.25f, 0.75f, 2f, 4f)));
            Assert.That(parameters.Motion.W, Is.EqualTo(-9.8f));
            Assert.That(defaults.Flags, Is.EqualTo(EmitterParams2D.FlagWorldSpace),
                "a default group sets only the world-space bit");
        });

        // 2D stretch requires the velocity alignment; without it no bit is set.
        var unaligned = new ParticleGroup2DAsset { VelocityStretch = true };
        Assert.That(EmitterParams2D.FromAsset(unaligned, 6).Flags & EmitterParams2D.FlagVelocityStretch, Is.Zero);

        // A 2D group authoring a depth state opts into the pass's depth-base
        // world z (depth base minus particle height); a group without one keeps
        // the default z of 0 (no flag, no depth test).
        var depthRead = new ParticleGroup2DAsset { Depth = DepthStencilState.Read };
        Assert.That(EmitterParams2D.FromAsset(depthRead, 6).Flags & EmitterParams2D.FlagDepthBase, Is.Not.Zero);
        Assert.That(EmitterParams2D.FromAsset(new ParticleGroup2DAsset(), 6).Flags & EmitterParams2D.FlagDepthBase, Is.Zero);

        // 3D stretch stands alone (billboards have no align-rotation mode).
        var stretched3D = new ParticleGroup3DAsset { VelocityStretch = true, StretchSpeedScale = 0.2f };
        EmitterParams3D parameters3D = EmitterParams3D.FromAsset(stretched3D, 6);
        Assert.Multiple(() =>
        {
            Assert.That(parameters3D.Flags & EmitterParams3D.FlagVelocityStretch, Is.Not.Zero);
            Assert.That(parameters3D.Speed.W, Is.EqualTo(0.2f));
            Assert.That(parameters3D.Size.W, Is.EqualTo(1f));
        });

        // Strict members: an unknown field fails the parse.
        string bad = Effect2DJson.Replace("\"emissionRate\"", "\"emissionRateTypo\"");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ParticleEffectAsset>(bad, options));
    }

    [Test]
    public void GroupMaterialLoadsAnAmatAssetWithSharedResources()
    {
        using EngineHost engine = new(GameEngineSetting.CreateNoGPU());
        JsonSerializerOptions options = AssetLoaderParticleEffect.CreateJsonOptions(
            engine.AssetSystem, engine.RenderingSystem.ShaderSystem);

        // The group's material is an .amat reference (surface module + shared
        // textures + uniform parameters); the group derives its own texture over
        // the surface's "texture" slot.
        const string json = """
            {
                "$type": "Alco.Particles.ParticleEffect2DAsset",
                "version": "1.0",
                "groups": [
                    {
                        "name": "Dissolve",
                        "material": "Materials/TestParticleMat.amat",
                        "texture": "TestNoise",
                        "blend": "Additive",
                        "flipbook": { "rows": 2, "cols": 2 }
                    }
                ]
            }
            """;

        ParticleEffectAsset asset = JsonSerializer.Deserialize<ParticleEffectAsset>(json, options)
            ?? throw new InvalidDataException("empty");
        ParticleEffect2DAsset effect = asset as ParticleEffect2DAsset
            ?? throw new AssertionException("not a 2D effect");
        ParticleGroup2DAsset group = effect.Groups[0];

        Assert.Multiple(() =>
        {
            Assert.That(group.Material, Is.Not.Null);
            Assert.That(group.Material!.Surface, Is.Not.Null);
            Assert.That(group.Material.Surface!.Name, Is.EqualTo("TestParticleSurface"));
            Assert.That(group.Material.Textures.ContainsKey("noiseTexture"), Is.True);
            Assert.That(group.Material.Parameters.ContainsKey("edgeWidth"), Is.True);
            Assert.That(group.Material.Parameters.ContainsKey("edgeColor"), Is.True);
            Assert.That(group.Texture, Is.Not.Null);
            Assert.That(group.Blend, Is.Not.Null);
            Assert.That(group.Flipbook, Is.Not.Null);
            Assert.That(group.Flipbook!.Cols, Is.EqualTo(2));
        });
    }

    [Test]
    public void GpuTwinsKeepTheirExpectedLayout()
    {
        // The slang twins (AlcoParticles_Core{2D,3D}.slang) document the same
        // sizes; a mismatch here means a struct drifted out of sync. The emitter
        // records must additionally stay a multiple of 16 bytes: the buffer
        // element stride rounds up to the struct alignment (16, from the
        // matrix), so a CPU size below that shifts every emitter's parameters.
        // The PositionOffset pins verify the vector members land on the offsets
        // every backend agrees on (scalars 4-aligned, float2 at 8, float3 at 16).
        Assert.Multiple(() =>
        {
            Assert.That(Marshal.SizeOf<GpuParticle2D>(), Is.EqualTo(92));
            Assert.That(Marshal.SizeOf<GpuParticle3D>(), Is.EqualTo(84));
            Assert.That(Marshal.SizeOf<EmitterParams2D>(), Is.EqualTo(368));
            Assert.That(Marshal.SizeOf<EmitterParams3D>(), Is.EqualTo(336));
            Assert.That(Marshal.OffsetOf<EmitterParams2D>(nameof(EmitterParams2D.PositionOffset)), Is.EqualTo(new IntPtr(352)));
            Assert.That(Marshal.OffsetOf<EmitterParams3D>(nameof(EmitterParams3D.PositionOffset)), Is.EqualTo(new IntPtr(320)));
        });
    }

    [Test]
    public void FlipbookReverseSetsTheReverseFlagBit()
    {
        // Sheets authored for remaining-lifetime playback (full flame at death)
        // play in reverse; the bit sits at the same position in 2D and 3D.
        var reverse = new ParticleFlipbook { Rows = 4, Cols = 4, Cycles = 1f, Reverse = true };
        var forward = new ParticleFlipbook { Rows = 4, Cols = 4, Cycles = 1f };
        Assert.Multiple(() =>
        {
            Assert.That(
                EmitterParams2D.FromAsset(new ParticleGroup2DAsset { Flipbook = reverse }, 6).Flags
                    & EmitterParams2D.FlagFlipbookReverse,
                Is.Not.Zero);
            Assert.That(
                EmitterParams2D.FromAsset(new ParticleGroup2DAsset { Flipbook = forward }, 6).Flags
                    & EmitterParams2D.FlagFlipbookReverse,
                Is.Zero);
            Assert.That(
                EmitterParams2D.FromAsset(new ParticleGroup2DAsset(), 6).Flags
                    & EmitterParams2D.FlagFlipbookReverse,
                Is.Zero, "no flipbook, no reverse bit");
            Assert.That(
                EmitterParams3D.FromAsset(new ParticleGroup3DAsset { Flipbook = reverse }, 6).Flags
                    & EmitterParams3D.FlagFlipbookReverse,
                Is.Not.Zero, "3D shares the bit position");
            Assert.That(
                EmitterParams3D.FromAsset(new ParticleGroup3DAsset { Flipbook = forward }, 6).Flags
                    & EmitterParams3D.FlagFlipbookReverse,
                Is.Zero);
        });
    }

    [Test]
    public void FlipbookCyclesAreLifetimeRelative()
    {
        // Cycles scale the anim against each particle's own lifetime: 1 plays
        // every frame exactly once from spawn to death (per particle), 2 plays
        // it twice; packed into the flipbook vector's z lane in 2D and 3D.
        var flipbook = new ParticleFlipbook { Rows = 4, Cols = 4, Cycles = 2f };
        Assert.Multiple(() =>
        {
            Assert.That(
                EmitterParams2D.FromAsset(new ParticleGroup2DAsset { Flipbook = flipbook }, 6).Flipbook.Z,
                Is.EqualTo(2f));
            Assert.That(
                EmitterParams3D.FromAsset(new ParticleGroup3DAsset { Flipbook = flipbook }, 6).Flipbook.Z,
                Is.EqualTo(2f));
            Assert.That(new ParticleFlipbook().Cycles, Is.EqualTo(1f),
                "one play-through per lifetime by default");
        });

        // FramesPerAnim splits a variant sheet into anims (w lane, clamped to
        // the sheet); 0 keeps the whole sheet as one animation.
        var sheet = new ParticleFlipbook { Rows = 8, Cols = 8, FramesPerAnim = 8 };
        var clamped = new ParticleFlipbook { Rows = 4, Cols = 4, FramesPerAnim = 99 };
        Assert.Multiple(() =>
        {
            Assert.That(
                EmitterParams2D.FromAsset(new ParticleGroup2DAsset { Flipbook = sheet }, 6).Flipbook.W,
                Is.EqualTo(8f));
            Assert.That(
                EmitterParams3D.FromAsset(new ParticleGroup3DAsset { Flipbook = sheet }, 6).Flipbook.W,
                Is.EqualTo(8f));
            Assert.That(
                EmitterParams2D.FromAsset(new ParticleGroup2DAsset { Flipbook = clamped }, 6).Flipbook.W,
                Is.EqualTo(16f), "clamped to rows x cols");
            Assert.That(new ParticleFlipbook().FramesPerAnim, Is.EqualTo(0),
                "whole sheet by default");
        });

        // The parser accepts the cycles/framesPerAnim members and rejects the
        // retired fps one (strict unmapped-member policy).
        using EngineHost engine = new(GameEngineSetting.CreateNoGPU());
        JsonSerializerOptions options = AssetLoaderParticleEffect.CreateJsonOptions(
            engine.AssetSystem, engine.RenderingSystem.ShaderSystem);
        const string json = """
            {
                "$type": "Alco.Particles.ParticleEffect2DAsset",
                "version": "1.0",
                "groups": [ { "name": "Smoke", "flipbook": { "rows": 8, "cols": 8, "cycles": 1, "framesPerAnim": 8 } } ]
            }
            """;
        var effect = JsonSerializer.Deserialize<ParticleEffectAsset>(json, options) as ParticleEffect2DAsset;
        Assert.Multiple(() =>
        {
            Assert.That(effect!.Groups[0].Flipbook!.Cycles, Is.EqualTo(1f));
            Assert.That(effect.Groups[0].Flipbook!.FramesPerAnim, Is.EqualTo(8));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ParticleEffectAsset>(
                json.Replace("\"cycles\": 1", "\"fps\": 24"), options));
        });
    }
}
