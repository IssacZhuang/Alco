using System.Text.RegularExpressions;
using Alco.Graphics;
using Alco.Graphics.Spirv;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Restores the descriptor sets encoded by the engine's <c>DEFINE_*</c>
/// resource macros. Slang cannot consume the set-only HLSL register syntax
/// used by the DXC path, so Core.hlsli lets it allocate a temporary flat
/// layout. This pass moves both SPIR-V decorations and engine reflection back
/// to the source-declared sets and sequential per-set bindings.
/// </summary>
internal static class SlangBindingRemapper
{
    internal readonly record struct Location(uint Set, uint Binding);

    private static readonly Regex ResourceDeclaration = new(
        @"^\s*DEFINE_(?<kind>[A-Z0-9_]+)\s*\((?<args>[^)\r\n]+)\)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<string, Location> ParseSourceLayout(string source)
    {
        Dictionary<string, Location> locations = new(StringComparer.Ordinal);
        Dictionary<uint, uint> nextBindings = [];

        foreach (Match match in ResourceDeclaration.Matches(source))
        {
            string kind = match.Groups["kind"].Value;
            string[] arguments = match.Groups["args"].Value
                .Split(',', StringSplitOptions.TrimEntries);
            if (arguments.Length < 2 || !uint.TryParse(arguments[0], out uint set))
            {
                continue;
            }

            string name = kind == "STORAGE" && arguments.Length >= 3
                ? arguments[2]
                : arguments[1];
            if (locations.ContainsKey(name))
            {
                continue;
            }

            uint binding = nextBindings.GetValueOrDefault(set);
            locations.Add(name, new Location(set, binding++));
            if (kind is "TEX2D_SAMPLE" or "TEX3D_SAMPLE" or "TEX2D_DEPTH_SAMPLE")
            {
                locations.Add(name + ShaderReflectionInfo.SamplerSuffix, new Location(set, binding++));
            }
            nextBindings[set] = binding;
        }

        return locations;
    }

    public static ShaderReflectionInfo RemapReflection(
        ShaderReflectionInfo reflection,
        IReadOnlyDictionary<string, Location> sourceLayout)
    {
        Dictionary<uint, List<BindGroupEntryInfo>> groups = [];
        foreach (BindGroupLayout layout in reflection.BindGroups)
        {
            foreach (BindGroupEntryInfo info in layout.Bindings)
            {
                Location location = sourceLayout.TryGetValue(info.Entry.Name, out Location declared)
                    ? declared
                    : new Location(layout.Group, info.Entry.Binding);
                if (!groups.TryGetValue(location.Set, out List<BindGroupEntryInfo>? bindings))
                {
                    bindings = [];
                    groups.Add(location.Set, bindings);
                }

                BindGroupEntryInfo remapped = info;
                remapped.Entry = info.Entry with { Binding = location.Binding };
                bindings.Add(remapped);
            }
        }

        if (groups.Count == 0)
        {
            return reflection;
        }

        uint maxSet = groups.Keys.Max();
        List<BindGroupLayout> layouts = new((int)maxSet + 1);
        for (uint set = 0; set <= maxSet; set++)
        {
            if (!groups.TryGetValue(set, out List<BindGroupEntryInfo>? bindings))
            {
                throw new ShaderValidationException(
                    $"Slang source resource layout has an empty descriptor set {set}.");
            }
            bindings.Sort((a, b) => a.Entry.Binding.CompareTo(b.Entry.Binding));
            layouts.Add(new BindGroupLayout { Group = set, Bindings = bindings.ToArray() });
        }

        return new ShaderReflectionInfo(
            reflection.VertexLayouts,
            layouts,
            reflection.PushConstantsRanges,
            reflection.Size,
            reflection.FragmentOutputCount);
    }

    public static byte[] RemapSpirv(
        byte[] spirv,
        ShaderReflectionInfo originalReflection,
        IReadOnlyDictionary<string, Location> sourceLayout)
    {
        Dictionary<Location, Location> remap = [];
        foreach (BindGroupLayout layout in originalReflection.BindGroups)
        {
            foreach (BindGroupEntryInfo info in layout.Bindings)
            {
                if (sourceLayout.TryGetValue(info.Entry.Name, out Location declared))
                {
                    remap[new Location(layout.Group, info.Entry.Binding)] = declared;
                }
            }
        }
        if (remap.Count == 0)
        {
            return spirv;
        }

        SpirvModule module = SpirvReader.Parse(spirv);
        Dictionary<uint, Location> variables = [];
        foreach (KeyValuePair<uint, List<SpirvDecorationEntry>> pair in module.Decorations)
        {
            if (!module.HasDecoration(pair.Key, SpirvDecoration.DescriptorSet) ||
                !module.HasDecoration(pair.Key, SpirvDecoration.Binding))
            {
                continue;
            }
            variables[pair.Key] = new Location(
                module.GetDecorationValue(pair.Key, SpirvDecoration.DescriptorSet),
                module.GetDecorationValue(pair.Key, SpirvDecoration.Binding));
        }

        bool changed = false;
        foreach (SpirvInstruction instruction in module.Instructions)
        {
            if (instruction.OpCode != (ushort)SpirvOp.Decorate ||
                !variables.TryGetValue(instruction[1], out Location current) ||
                !remap.TryGetValue(current, out Location target))
            {
                continue;
            }

            if (instruction[2] == (uint)SpirvDecoration.DescriptorSet)
            {
                instruction[3] = target.Set;
                changed = true;
            }
            else if (instruction[2] == (uint)SpirvDecoration.Binding)
            {
                instruction[3] = target.Binding;
                changed = true;
            }
        }

        return changed ? module.ToBytes() : spirv;
    }
}
