using Alco.Graphics;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

/// <summary>
/// Pins the parameter-interface contracts the depth-aware FXAA mode axis
/// (<c>MainPS&lt;let Quality : int, let Mode : int&gt;</c>) leans on:
/// <list type="number">
/// <item>the depth texture field of a <c>ParameterBlock</c> survives in every
/// specialization's reflection — even a mode that never references it — at a
/// stable set/binding, so the C# material binds it once per blit regardless
/// of the active mode;</item>
/// <item>the uniform block layout is specialization-independent, so one
/// <c>UniformGraphicsBuffer</c> created from one specialization serves every
/// (quality, mode) pair;</item>
/// <item>the modes do fold to different code (the luma mode drops the depth
/// loads), so the axis is a real compile-time specialization, not a runtime
/// branch.</item>
/// </list>
/// A slang upgrade that changes any of this must fail here, not inside a game
/// material compile.
/// </summary>
[TestFixture]
public class FxaaModeSpecializationTest
{
    // The FXAA module's parameter shape: color + conditionally-referenced
    // depth texture in one ParameterBlock, the tunables in another, and a
    // two-axis generic fragment entry.
    private const string FxaaShapeShader = """
        public struct PassParams
        {
            public Texture2D colorTexture;
            public DepthTexture2D depthTexture;
        };
        public ParameterBlock<PassParams> pass;

        public struct FxaaData
        {
            public float2 invFrameSize;
            public float threshold;
            public float subpix;
        };
        public ParameterBlock<FxaaData> fxaaData;

        [shader("fragment")]
        float4 MainPS<let quality : int, let mode : int>(float4 fragCoord : SV_POSITION) : SV_TARGET
        {
            float3 rgbM = pass.colorTexture.Load(int3(int2(fragCoord.xy), 0)).rgb;
            float luma = rgbM.y + fxaaData.invFrameSize.x + fxaaData.threshold + fxaaData.subpix;
            if (mode == 1)
            {
                float dM = pass.depthTexture.Load(int3(int2(fragCoord.xy), 0));
                luma += dM;
            }
            if (mode == 2)
            {
                luma *= 0.5f;
            }
            return float4(luma, 0.0f, 0.0f, 1.0f);
        }
        """;

    private static SlangProgram Link(params string[] args)
    {
        var compiler = new SlangCompiler();
        SlangCompileSession session = compiler.CreateSession(new SlangCompilerOptions());
        SlangModuleHandle module = session.LoadModuleFromSource(
            "alco_fxaa_mode_probe", "alco_fxaa_mode_probe.slang", FxaaShapeShader);
        return session.CompileAllEntryPoints(module, args);
    }

    [Test]
    public void DepthTextureSlot_SurvivesInEveryMode_AtStableBinding()
    {
        using SlangProgram luma = Link("1", "0");
        using SlangProgram depthAssisted = Link("1", "1");
        using SlangProgram depthOnly = Link("1", "2");

        (string Label, SlangProgram Program)[] modes =
        [
            ("luma", luma),
            ("depth-assisted", depthAssisted),
            ("depth-only", depthOnly),
        ];
        foreach ((string label, SlangProgram program) in modes)
        {
            Assert.That(program.Reflection.TryGetResourceLocation("depthTexture", out ShaderResourceLocation location),
                Is.True, $"the depth texture slot must exist in the {label} specialization");
            Assert.That((location.GroupIndex, location.Binding), Is.EqualTo((0u, 1u)),
                $"the depth texture binding must be stable in the {label} specialization");
        }
    }

    [Test]
    public void UniformBlockLayout_IsSpecializationIndependent()
    {
        using SlangProgram luma = Link("1", "0");
        using SlangProgram depthAssisted = Link("1", "1");
        using SlangProgram depthOnly = Link("1", "2");

        AssertUniformLayoutEqual(luma, depthAssisted);
        AssertUniformLayoutEqual(luma, depthOnly);
    }

    [Test]
    public void Modes_FoldToDistinctCode()
    {
        using SlangProgram luma = Link("1", "0");
        using SlangProgram depthAssisted = Link("1", "1");
        using SlangProgram depthOnly = Link("1", "2");

        Assert.That(luma.EntryCode[0].Length, Is.GreaterThan(4));
        Assert.That(depthAssisted.EntryCode[0].Length, Is.GreaterThan(4));
        Assert.That(depthOnly.EntryCode[0].Length, Is.GreaterThan(4));
        Assert.That(depthAssisted.EntryCode[0].Length, Is.Not.EqualTo(luma.EntryCode[0].Length),
            "the depth-assisted mode must fold in the depth loads the luma mode drops");
        Assert.That(depthOnly.EntryCode[0].Length, Is.Not.EqualTo(luma.EntryCode[0].Length),
            "each mode is its own specialization");
    }

    private static void AssertUniformLayoutEqual(SlangProgram a, SlangProgram b)
    {
        var membersA = a.Reflection.GetUniformMembers("fxaaData");
        var membersB = b.Reflection.GetUniformMembers("fxaaData");
        Assert.That(membersB.Count, Is.EqualTo(membersA.Count), "fxaaData member count must be specialization-independent");
        for (int i = 0; i < membersA.Count; i++)
        {
            Assert.That(membersB[i].Name, Is.EqualTo(membersA[i].Name), "member name");
            Assert.That(membersB[i].OffsetBytes, Is.EqualTo(membersA[i].OffsetBytes), $"member '{membersA[i].Name}' offset");
        }
    }
}
