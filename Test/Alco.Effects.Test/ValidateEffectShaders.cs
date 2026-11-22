using System.Runtime.InteropServices;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Effects.Test;

/// <summary>
/// Validates all built-in and fixture <c>Alco.Effects</c> Slang shaders across every
/// supported <see cref="ShaderTarget"/> and verifies C-layout ABI alignment with C# structs.
/// </summary>
[TestFixture]
public sealed unsafe class ValidateEffectShaders
{
    private static readonly ShaderTarget[] SupportedTargets =
    [
        ShaderTarget.SpirvCompact,
        ShaderTarget.Spirv,
        ShaderTarget.Dxil,
        ShaderTarget.MetalSource,
    ];

    /// <summary>Standalone compute shaders compile cleanly across every target and match their root layouts.</summary>
    [TestCaseSource(nameof(SupportedTargets))]
    public void StandaloneComputeShadersCompileAcrossAllTargets(ShaderTarget target)
    {
        using var compiler = CreateCompiler(target);

        CompiledShader reset = compiler.Compile("GpuParticleResetArgs", ShaderPipelineKind.Compute);
        Assert.That(reset.Target, Is.EqualTo(target));
        Assert.That(reset.Stages, Has.Count.EqualTo(1));
        Assert.That(reset.Reflection.Root, Is.Not.Null);
        Assert.That(reset.Reflection.Root!.Find("drawArgs")?.SizeBytes, Is.EqualTo(8u));
        Assert.That(reset.Reflection.Root.Find("drawCount")?.SizeBytes, Is.EqualTo(4u));

        foreach (string initModule in new[] { "GpuParticleInit2D", "GpuParticleInit3D" })
        {
            CompiledShader init = compiler.Compile(initModule, ShaderPipelineKind.Compute);
            Assert.That(init.Stages, Has.Count.EqualTo(1));
            Assert.That(init.Reflection.Root, Is.Not.Null);
            Assert.That(init.Reflection.Root!.Find("particles")?.SizeBytes, Is.EqualTo(8u));
            Assert.That(init.Reflection.Root.Find("sliceOffset")?.SizeBytes, Is.EqualTo(4u));
            Assert.That(init.Reflection.Root.Find("count")?.SizeBytes, Is.EqualTo(4u));
        }
    }

    /// <summary>Composed emit and simulate compute pipelines compile across every target for default and custom behaviors.</summary>
    [TestCaseSource(nameof(SupportedTargets))]
    public void ComposedParticleBehaviorShadersCompileAcrossAllTargets(ShaderTarget target)
    {
        using var compiler = CreateCompiler(target);

        (string Template, string Behavior)[] cases =
        [
            ("GpuParticleEmit2D", "AlcoParticles_Default2D"),
            ("GpuParticleEmit2D", "TestBehavior2D"),
            ("GpuParticleEmit3D", "AlcoParticles_Default3D"),
            ("GpuParticleEmit3D", "TestBehavior3D"),
            ("GpuParticleSimulate2D", "AlcoParticles_Default2D"),
            ("GpuParticleSimulate2D", "TestBehavior2D"),
            ("GpuParticleSimulate3D", "AlcoParticles_Default3D"),
            ("GpuParticleSimulate3D", "TestBehavior3D"),
        ];

        foreach ((string template, string behavior) in cases)
        {
            CompiledShader compiled = compiler.CompileComposed(template, behavior, ShaderPipelineKind.Compute);
            Assert.That(compiled.Target, Is.EqualTo(target));
            Assert.That(compiled.Stages, Has.Count.EqualTo(1), $"{template}+{behavior}");
            Assert.That(compiled.Reflection.Root, Is.Not.Null);
            Assert.That(compiled.Reflection.Root!.Find(ParticleShaderKeys.Particles), Is.Not.Null, $"{template}+{behavior}");
            Assert.That(compiled.Reflection.Root.Find(ParticleShaderKeys.Emitters), Is.Not.Null, $"{template}+{behavior}");
            Assert.That(compiled.Reflection.Root.Find(ParticleShaderKeys.WorkBlocks), Is.Not.Null, $"{template}+{behavior}");
            Assert.That(compiled.Reflection.Root.Find(ParticleShaderKeys.WorkBlockBase), Is.Not.Null, $"{template}+{behavior}");
        }
    }

    /// <summary>Composed 2D/3D particle and trail graphics shaders compile across every target with valid Pass and Material roots.</summary>
    [TestCaseSource(nameof(SupportedTargets))]
    public void ComposedGraphicsShadersCompileAcrossAllTargets(ShaderTarget target)
    {
        using var compiler = CreateCompiler(target);

        (string Template, string Surface, string? ProbeField)[] cases =
        [
            ("GpuParticle2D", "ParticleSurfaceDefault", null),
            ("GpuParticle2D", "TestParticleSurface", "Material.edgeWidth"),
            ("GpuParticle3D", "ParticleSurfaceDefault", null),
            ("GpuParticle3D", "TestParticleSurface", "Material.noiseScale"),
            ("GpuTrail2D", "TrailSurfaceDefault", null),
            ("GpuTrail2D", "TrailSurfaceSmoke", "Material.smokeColor"),
            ("GpuTrail2D", "TrailContractProbe", null),
            ("GpuTrail2D", "TrailInheritedSurface", null),
            ("GpuTrail3D", "TrailSurfaceDefault", null),
            ("GpuTrail3D", "TrailSurfaceSmoke", "Material.smokeColor"),
            ("GpuTrail3D", "TrailContractProbe", null),
            ("GpuTrail3D", "TrailInheritedSurface", null),
        ];

        foreach ((string template, string surface, string? probeField) in cases)
        {
            CompiledShader compiled = compiler.CompileComposed(template, surface, ShaderPipelineKind.Graphics);
            Assert.That(compiled.Target, Is.EqualTo(target));
            Assert.That(compiled.Stages, Has.Count.EqualTo(2), $"{template}+{surface}");
            Assert.That(compiled.Reflection.Root, Is.Not.Null);
            Assert.That(compiled.Reflection.Root!.Find(ParticleShaderKeys.Pass), Is.Not.Null,
                $"{template}+{surface} missing Pass root field");
            if (probeField != null)
            {
                Assert.That(compiled.Reflection.Root.Find(probeField), Is.Not.Null,
                    $"{template}+{surface} missing {probeField}");
            }
        }
    }

    /// <summary>Verifies C# unmanaged struct sizes and Slang C-layout ABI offsets match byte-for-byte.</summary>
    [Test]
    public void ShaderStructsMatchCSharpCLayout()
    {
        Assert.Multiple(() =>
        {
            Assert.That(sizeof(EmitterParams2D), Is.EqualTo(368));
            Assert.That(sizeof(EmitterParams3D), Is.EqualTo(336));
            Assert.That(sizeof(GpuParticle2D), Is.EqualTo(92));
            Assert.That(sizeof(GpuParticle3D), Is.EqualTo(84));
            Assert.That(sizeof(GpuDrawArgs), Is.EqualTo(16));
            Assert.That(sizeof(GpuInstanceData), Is.EqualTo(8));
            Assert.That(sizeof(GpuParticleWorkBlock), Is.EqualTo(16));
            Assert.That(sizeof(GpuTrailSystem2D.TrailPoint2D), Is.EqualTo(32));
            Assert.That(sizeof(GpuTrailSystem2D.TrailParams2D), Is.EqualTo(96));
            Assert.That(sizeof(GpuTrailSystem3D.TrailPoint3D), Is.EqualTo(32));
            Assert.That(sizeof(GpuTrailSystem3D.TrailParams3D), Is.EqualTo(96));
            Assert.That(sizeof(GpuTrailSystem2D.TrailSegment), Is.EqualTo(8));
        });

        const string probeSource = """
            #language slang 2025
            module EffectsLayoutProbe;
            import Core;
            import AlcoParticles_Core2D;
            import AlcoParticles_Core3D;
            import AlcoRendering_Trails2D;
            import AlcoRendering_Trails3D;

            [GPURoot]
            public struct AbiProbeRoot
            {
                public EmitterParams2D E2;
                public EmitterParams3D E3;
                public ParticleData2D P2;
                public ParticleData3D P3;
                public GpuDrawArgs DrawArgs;
                public GpuInstanceData InstanceData;
                public GpuWorkBlock WorkBlock;
                public TrailPoint2D TP2;
                public TrailParams2D TR2;
                public TrailPoint3D TP3;
                public TrailParams3D TR3;
                public TrailSegment Seg;
                public GPUBufferView Sink;
            };

            [shader("compute")]
            [numthreads(1, 1, 1)]
            public void mainCS()
            {
                AbiProbeRoot root = gpu_load_root<AbiProbeRoot>();
                gpu_store(root.Sink, 0u, root.E2.spriteTextureId + root.E3.sizeCurveTextureId
                    + root.P2.animStart + root.P3.animStart + root.DrawArgs.instanceCount
                    + root.InstanceData.particleIndex + root.WorkBlock.threadBase
                    + uint(root.TP2.emitTime) + root.TR2.ring.w
                    + uint(root.TP3.emitTime) + root.TR3.ring.w + root.Seg.segment);
            }
            """;

        using var compiler = CreateCompiler(ShaderTarget.SpirvCompact);
        CompiledShader probe = compiler.CompileSource("EffectsLayoutProbe", "EffectsLayoutProbe.slang", probeSource, ShaderPipelineKind.Compute);
        ShaderRootLayout root = probe.Reflection.Root!;

        Assert.Multiple(() =>
        {
            Assert.That(root.Find("E2")?.SizeBytes, Is.EqualTo((uint)sizeof(EmitterParams2D)));
            Assert.That(root.Find("E3")?.SizeBytes, Is.EqualTo((uint)sizeof(EmitterParams3D)));
            Assert.That(root.Find("P2")?.SizeBytes, Is.EqualTo((uint)sizeof(GpuParticle2D)));
            Assert.That(root.Find("P3")?.SizeBytes, Is.EqualTo((uint)sizeof(GpuParticle3D)));
            Assert.That(root.Find("DrawArgs")?.SizeBytes, Is.EqualTo((uint)sizeof(GpuDrawArgs)));
            Assert.That(root.Find("InstanceData")?.SizeBytes, Is.EqualTo((uint)sizeof(GpuInstanceData)));
            Assert.That(root.Find("WorkBlock")?.SizeBytes, Is.EqualTo((uint)sizeof(GpuParticleWorkBlock)));
            Assert.That(root.Find("TP2")?.SizeBytes, Is.EqualTo((uint)sizeof(GpuTrailSystem2D.TrailPoint2D)));
            Assert.That(root.Find("TR2")?.SizeBytes, Is.EqualTo((uint)sizeof(GpuTrailSystem2D.TrailParams2D)));
            Assert.That(root.Find("TP3")?.SizeBytes, Is.EqualTo((uint)sizeof(GpuTrailSystem3D.TrailPoint3D)));
            Assert.That(root.Find("TR3")?.SizeBytes, Is.EqualTo((uint)sizeof(GpuTrailSystem3D.TrailParams3D)));
            Assert.That(root.Find("Seg")?.SizeBytes, Is.EqualTo((uint)sizeof(GpuTrailSystem2D.TrailSegment)));

            uint e2Base = root.Find("E2")!.OffsetBytes;
            Assert.That(root.Find("E2.worldMatrix")!.OffsetBytes - e2Base, Is.EqualTo((uint)Marshal.OffsetOf<EmitterParams2D>(nameof(EmitterParams2D.WorldMatrix))));
            Assert.That(root.Find("E2.spriteTextureId")!.OffsetBytes - e2Base, Is.EqualTo((uint)Marshal.OffsetOf<EmitterParams2D>(nameof(EmitterParams2D.SpriteTextureId))));
            Assert.That(root.Find("E2.colorGradientTextureId")!.OffsetBytes - e2Base, Is.EqualTo((uint)Marshal.OffsetOf<EmitterParams2D>(nameof(EmitterParams2D.ColorGradientTextureId))));
            Assert.That(root.Find("E2.sizeCurveTextureId")!.OffsetBytes - e2Base, Is.EqualTo((uint)Marshal.OffsetOf<EmitterParams2D>(nameof(EmitterParams2D.SizeCurveTextureId))));

            uint e3Base = root.Find("E3")!.OffsetBytes;
            Assert.That(root.Find("E3.worldMatrix")!.OffsetBytes - e3Base, Is.EqualTo((uint)Marshal.OffsetOf<EmitterParams3D>(nameof(EmitterParams3D.WorldMatrix))));
            Assert.That(root.Find("E3.spriteTextureId")!.OffsetBytes - e3Base, Is.EqualTo((uint)Marshal.OffsetOf<EmitterParams3D>(nameof(EmitterParams3D.SpriteTextureId))));
            Assert.That(root.Find("E3.colorGradientTextureId")!.OffsetBytes - e3Base, Is.EqualTo((uint)Marshal.OffsetOf<EmitterParams3D>(nameof(EmitterParams3D.ColorGradientTextureId))));
            Assert.That(root.Find("E3.sizeCurveTextureId")!.OffsetBytes - e3Base, Is.EqualTo((uint)Marshal.OffsetOf<EmitterParams3D>(nameof(EmitterParams3D.SizeCurveTextureId))));
            Assert.That(root.Find("E3.reserved3")!.OffsetBytes - e3Base, Is.EqualTo((uint)Marshal.OffsetOf<EmitterParams3D>(nameof(EmitterParams3D.Reserved3))));
        });
    }

    private static ShaderCompiler CreateCompiler(ShaderTarget target)
    {
        var compiler = new ShaderCompiler(target, EffectsTestHost.CreateCompilerOptions());
        for (int i = 0; i < AlcoEffectsShaders.BuiltInModuleOrder.Count; i++)
        {
            string moduleName = AlcoEffectsShaders.BuiltInModuleOrder[i];
            compiler.LoadModuleFromSource(moduleName, moduleName + ".slang",
                AlcoEffectsShaders.LoadEmbeddedSource(moduleName + ".slang"));
        }
        return compiler;
    }
}
