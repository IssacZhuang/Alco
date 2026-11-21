using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.ImGUI.Test;

/// <summary>
/// Compiles and validates the embedded <c>ImGUI.slang</c> shader and root layout across targets.
/// </summary>
[TestFixture]
public sealed class TestImGuiShader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedImGuiRoot
    {
        public Vector2 Scale;
        public Vector2 Translate;
        public GPUBufferView Vertices;
        public uint Texture;
        private uint _padding0;
    }

    /// <summary>
    /// Verifies that <c>ImGUI.slang</c> compiles with valid bindless ABI and expected root/vertex layouts.
    /// </summary>
    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Dxil)]
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.MetalSource)]
    public void ImGuiShader_CompilesAndReflectsExpectedAbi(ShaderTarget target)
    {
        if (!ShaderCompiler.IsTargetSupported(target))
        {
            Assert.Ignore($"Shader target {target} is not supported on this host.");
            return;
        }

        string source = ImGUIResourceHelper.GetEmbeddedResourceString("ImGUI.slang");
        using ShaderCompiler compiler = new(target);
        CompiledShader compiled = compiler.CompileSource("ImGUI", "ImGUI.slang", source, ShaderPipelineKind.Graphics);

        Assert.That(compiled.Stages, Has.Count.EqualTo(2));
        Assert.That(compiled.Reflection.FindEntryPoint("MainVS")?.Stage, Is.EqualTo(ShaderStageKind.Vertex));
        Assert.That(compiled.Reflection.FindEntryPoint("MainPS")?.Stage, Is.EqualTo(ShaderStageKind.Pixel));

        ShaderRootLayout? root = compiled.Reflection.Root;
        Assert.That(root, Is.Not.Null);
        Assert.That(root!.MatchesSize<ExpectedImGuiRoot>(), Is.True);
        Assert.That(root.SizeBytes, Is.EqualTo(32u));

        ShaderFieldLayout? scale = root.Find("Scale");
        ShaderFieldLayout? translate = root.Find("Translate");
        ShaderFieldLayout? vertices = root.Find("Vertices");
        ShaderFieldLayout? texture = root.Find("Texture");

        Assert.That(scale, Is.Not.Null);
        Assert.That(scale!.OffsetBytes, Is.EqualTo(0u));
        Assert.That(scale.SizeBytes, Is.EqualTo(8u));
        Assert.That(scale.Semantic, Is.EqualTo(ShaderFieldSemantic.Data));

        Assert.That(translate, Is.Not.Null);
        Assert.That(translate!.OffsetBytes, Is.EqualTo(8u));
        Assert.That(translate.SizeBytes, Is.EqualTo(8u));
        Assert.That(translate.Semantic, Is.EqualTo(ShaderFieldSemantic.Data));

        Assert.That(vertices, Is.Not.Null);
        Assert.That(vertices!.OffsetBytes, Is.EqualTo(16u));
        Assert.That(vertices.SizeBytes, Is.EqualTo(8u));
        Assert.That(vertices.Semantic, Is.EqualTo(ShaderFieldSemantic.BufferView));

        Assert.That(texture, Is.Not.Null);
        Assert.That(texture!.OffsetBytes, Is.EqualTo(24u));
        Assert.That(texture.SizeBytes, Is.EqualTo(4u));
        Assert.That(texture.Semantic, Is.EqualTo(ShaderFieldSemantic.TextureId));

        ShaderTypeLayout? vertexType = compiled.Reflection.FindType("ImGuiVertex");
        Assert.That(vertexType, Is.Not.Null);
        Assert.That(vertexType!.SizeBytes, Is.EqualTo((uint)Marshal.SizeOf<ImDrawVert>()));
    }
}
