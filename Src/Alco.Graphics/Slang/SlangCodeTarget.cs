// Internal native Slang code targets and backend entry-point naming rules.

namespace Alco.Graphics.Slang;

internal enum SlangCodeTarget
{
    Spirv = 0,
    Dxil = 1,
    Msl = 2,
    MetalLib = 3,
    SpirvCompact = 4,
}

internal static class SlangCodeTargetExtensions
{
    internal static string EntryPointName(this SlangCodeTarget target, string sourceName) => target switch
    {
        SlangCodeTarget.Spirv or SlangCodeTarget.SpirvCompact => "main",
        SlangCodeTarget.Dxil or SlangCodeTarget.Msl or SlangCodeTarget.MetalLib => sourceName,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };
}
