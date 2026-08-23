using Alco.Graphics;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// The code formats slang emits for wgpu's shader passthrough (plan §4.1):
// each runtime backend consumes exactly one of them — Vulkan/SPIR-V,
// D3D12/DXIL, Metal/MSL — so the target is a per-device constant that keys
// every cache and names every entry point.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The native code format one slang session emits.</summary>
public enum SlangCodeTarget
{
    /// <summary>SPIR-V 1.3 for wgpu's Vulkan backend (direct slang emission).</summary>
    Spirv = 0,

    /// <summary>DXIL containers for wgpu's D3D12 backend (slang's embedded DXC).</summary>
    Dxil = 1,

    /// <summary>MSL source for wgpu's Metal backend (slang's Metal codegen).</summary>
    Msl = 2,
}

public static class SlangCodeTargetExtensions
{
    /// <summary>The engine shader language each target's entry code is written in.</summary>
    public static ShaderLanguage Language(this SlangCodeTarget target) => target switch
    {
        SlangCodeTarget.Spirv => ShaderLanguage.SPIRV,
        SlangCodeTarget.Dxil => ShaderLanguage.DXIL,
        SlangCodeTarget.Msl => ShaderLanguage.MSL,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    /// <summary>
    /// The name wgpu must use to select the entry point: slang names every SPIR-V
    /// entry "main" regardless of the source function, while DXIL containers and
    /// MSL libraries keep the declared function names.
    /// </summary>
    public static string EntryPointName(this SlangCodeTarget target, string sourceName) => target switch
    {
        SlangCodeTarget.Spirv => "main",
        SlangCodeTarget.Dxil => sourceName,
        SlangCodeTarget.Msl => sourceName,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };
}
