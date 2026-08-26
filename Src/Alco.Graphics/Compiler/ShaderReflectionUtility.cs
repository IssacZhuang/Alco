namespace Alco.Graphics;

public static class ShaderReflectionUtility
{
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
    public static void ValidateBindGroupLayouts(ShaderReflection info, int maxBindGroups, string? shaderName = null)
    {
        IReadOnlyList<BindGroupLayout> bindGroups = info.BindGroups;
        int count = bindGroups.Count;

        if (count > maxBindGroups)
        {
            throw new ShaderReflectionException(
                $"Shader '{shaderName}' uses {count} bind groups (groups: {FormatGroupIndices(bindGroups)}), " +
                $"which exceeds the maximum {maxBindGroups}.");
        }

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
}
