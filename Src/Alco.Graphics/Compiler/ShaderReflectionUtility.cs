using Alco.Graphics.Spirv;

namespace Alco.Graphics;

public static class ShaderReflectionUtility
{
    public static ShaderReflectionInfo GetSpirvReflection(
        ReadOnlyMemory<byte> vertexSpirv, ReadOnlyMemory<byte> fragmentSpirv, bool useStandardStage = false)
    {
        ShaderReflectionInfo vertex = SpirvReflector.Reflect(vertexSpirv.Span, useStandardStage);
        ShaderReflectionInfo fragment = SpirvReflector.Reflect(fragmentSpirv.Span, useStandardStage);
        return MergeReflectionInfo(vertex, fragment);
    }

    public static ShaderReflectionInfo GetSpirvReflection(ReadOnlyMemory<byte> spirv, bool useStandardStage = false)
    {
        return SpirvReflector.Reflect(spirv.Span, useStandardStage);
    }

    /// <summary>
    /// Validates the bind group layout of a shader against the device constraints:
    /// the number of bind groups must not exceed <paramref name="maxBindGroups"/>, and the
    /// bind group (set) indices must be contiguous starting at 0 (no gaps and no duplicates).
    /// </summary>
    /// <param name="info">The shader reflection info to validate.</param>
    /// <param name="maxBindGroups">The maximum number of bind groups allowed by the device.</param>
    /// <param name="shaderName">The optional shader name included in error messages.</param>
    /// <exception cref="ShaderReflectionException">
    /// Thrown when the bind group count exceeds the limit or the indices are not contiguous from 0.
    /// </exception>
    public static void ValidateBindGroupLayouts(ShaderReflectionInfo info, int maxBindGroups, string? shaderName = null)
    {
        IReadOnlyList<BindGroupLayout> bindGroups = info.BindGroups;
        int count = bindGroups.Count;

        // Rule 1: the number of bind groups must not exceed the device limit.
        if (count > maxBindGroups)
        {
            throw new ShaderReflectionException(
                $"Shader '{shaderName}' uses {count} bind groups (groups: {FormatGroupIndices(bindGroups)}), " +
                $"which exceeds the maximum {maxBindGroups}.");
        }

        // Rule 2: bind group (set) indices must be contiguous starting at 0.
        // Sorting first lets a single pass detect a non-zero start, a gap, or a duplicate.
        uint[] indices = new uint[count];
        for (int i = 0; i < count; i++)
        {
            indices[i] = bindGroups[i].Group;
        }

        Array.Sort(indices);

        for (int i = 0; i < count; i++)
        {
            if (indices[i] != i)
            {
                throw new ShaderReflectionException(
                    $"Shader '{shaderName}' bind group indices must be contiguous starting at 0, " +
                    $"expected 0..{count - 1}, found [{FormatGroupIndices(bindGroups)}].");
            }
        }
    }

    private static string FormatGroupIndices(IReadOnlyList<BindGroupLayout> bindGroups)
    {
        System.Text.StringBuilder builder = new();
        for (int i = 0; i < bindGroups.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(bindGroups[i].Group);
        }

        return builder.ToString();
    }

    public static ShaderReflectionInfo MergeReflectionInfo(ShaderReflectionInfo vertex, ShaderReflectionInfo fragment)
    {
        Dictionary<uint, BindGroupLayout> bindGroups = new();

        foreach (BindGroupLayout layout in vertex.BindGroups)
        {
            bindGroups.Add(layout.Group, layout);
        }

        foreach (BindGroupLayout layout in fragment.BindGroups)
        {
            if (bindGroups.TryGetValue(layout.Group, out BindGroupLayout existing))
            {
                bindGroups[layout.Group] = new BindGroupLayout
                {
                    Group = layout.Group,
                    Bindings = MergeBindGroupEntries(existing.Bindings, layout.Bindings)
                };
            }
            else
            {
                bindGroups.Add(layout.Group, layout);
            }
        }

        // Each stage reflects its own push constant blocks; keep the larger of the two
        // stage lists (ShaderReflectionInfo aggregates the total size from the ranges).
        IReadOnlyList<PushConstantsRange> maxRangesList =
            vertex.PushConstantsRanges.Count >= fragment.PushConstantsRanges.Count
                ? vertex.PushConstantsRanges
                : fragment.PushConstantsRanges;

        PushConstantsRange[] ranges = new PushConstantsRange[maxRangesList.Count];
        for (int i = 0; i < maxRangesList.Count; i++)
        {
            ranges[i] = maxRangesList[i];
        }

        KeyValuePair<uint, BindGroupLayout>[] bindGroupsArray = bindGroups.ToArray();
        Array.Sort(bindGroupsArray, (a, b) => a.Key.CompareTo(b.Key));

        BindGroupLayout[] layouts = new BindGroupLayout[bindGroupsArray.Length];
        for (int i = 0; i < bindGroupsArray.Length; i++)
        {
            layouts[i] = bindGroupsArray[i].Value;
        }

        return new ShaderReflectionInfo(
            vertex.VertexLayouts,
            layouts,
            ranges,
            ThreadGroupSize.Default);
    }

    private static BindGroupEntryInfo[] MergeBindGroupEntries(
        IReadOnlyList<BindGroupEntryInfo> left, IReadOnlyList<BindGroupEntryInfo> right)
    {
        Dictionary<uint, BindGroupEntryInfo> bindings = new();
        foreach (BindGroupEntryInfo binding in left)
        {
            bindings.Add(binding.Entry.Binding, binding);
        }

        foreach (BindGroupEntryInfo binding in right)
        {
            if (bindings.TryGetValue(binding.Entry.Binding, out BindGroupEntryInfo existing))
            {
                existing.Entry.Stage |= binding.Entry.Stage;
                bindings[binding.Entry.Binding] = existing;
            }
            else
            {
                bindings.Add(binding.Entry.Binding, binding);
            }
        }

        return bindings.Values.ToArray();
    }
}
