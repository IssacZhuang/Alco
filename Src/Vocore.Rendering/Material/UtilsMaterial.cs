using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// Utility class for material.
/// </summary>
public static class UtilsMaterial
{
    /// <summary>
    /// Check if the bind group layout is a buffer group.
    /// The binding 0 must be a uniform buffer.
    /// </summary>
    /// <param name="bindings">The bindings to check</param>
    /// <returns>True if the bind group layout is a buffer group, false otherwise</returns>
    public static bool IsUniformBufferGroup(Span<BindGroupEntryInfo> bindings)
    {
        if (bindings.Length != 1)
        {
            return false;
        }

        return bindings[0].Entry.Type == BindingType.UniformBuffer;
    }

    /// <summary>
    /// Check if the bind group layout is a buffer group.
    /// The binding 0 must be a uniform buffer.
    /// </summary>
    /// <param name="bindings">The bindings to check</param>
    /// <returns>True if the bind group layout is a buffer group, false otherwise</returns>
    public static bool IsUniformBufferGroup(Span<BindGroupEntry> bindings)
    {
        if (bindings.Length != 1)
        {
            return false;
        }

        return bindings[0].Type == BindingType.UniformBuffer;
    }


    /// <summary>
    /// Check if the bind group layout is a storage buffer group.
    /// The binding 0 must be a storage buffer.
    /// </summary>
    /// <param name="bindings">The bindings to check</param>
    /// <returns>True if the bind group layout is a storage buffer group, false otherwise</returns>
    public static bool IsStorageBufferGroup(Span<BindGroupEntryInfo> bindings)
    {
        if (bindings.Length != 1)
        {
            return false;
        }

        return bindings[0].Entry.Type == BindingType.StorageBuffer;
    }

    /// <summary>
    /// Check if the bind group layout is a storage buffer group.
    /// The binding 0 must be a storage buffer.
    /// </summary>
    /// <param name="bindings">The bindings to check</param>
    /// <returns>True if the bind group layout is a storage buffer group, false otherwise</returns>
    public static bool IsStorageBufferGroup(Span<BindGroupEntry> bindings)
    {
        if (bindings.Length != 1)
        {
            return false;
        }

        return bindings[0].Type == BindingType.StorageBuffer;
    }

    /// <summary>
    /// Check if the bind group layout is a texture sampler group.
    /// The binding 0 must be a texture and binding 1 must be a sampler.
    /// </summary>
    /// <param name="bindings">The bindings to check</param>
    /// <returns>True if the bind group layout is a texture sampler group, false otherwise</returns>
    public static bool IsTextureSamplerGroup(Span<BindGroupEntryInfo> bindings)
    {
        if (bindings.Length != 2)
        {
            return false;
        }

        return bindings[0].Entry.Type == BindingType.Texture &&
        bindings[1].Entry.Type == BindingType.Sampler;
    }

    /// <summary>
    /// Check if the bind group layout is a texture sampler group.
    /// The binding 0 must be a texture and binding 1 must be a sampler.
    /// </summary>
    /// <param name="bindings">The bindings to check</param>
    /// <returns>True if the bind group layout is a texture sampler group, false otherwise</returns>
    public static bool IsTextureSamplerGroup(Span<BindGroupEntry> bindings)
    {
        if (bindings.Length != 2)
        {
            return false;
        }

        return bindings[0].Type == BindingType.Texture &&
        bindings[1].Type == BindingType.Sampler;
    }

    /// <summary>
    /// Check if the bind group layout is a storage texture group.
    /// The binding 0 must be a storage texture.
    /// </summary>
    /// <param name="bindings">The bindings to check</param>
    /// <returns>True if the bind group layout is a storage texture group, false otherwise</returns>
    public static bool IsStorageTextureGroup(Span<BindGroupEntryInfo> bindings)
    {
        if (bindings.Length != 1)
        {
            return false;
        }

        return bindings[0].Entry.Type == BindingType.StorageTexture;
    }

    /// <summary>
    /// Check if the bind group layout is a storage texture group.
    /// The binding 0 must be a storage texture.
    /// </summary>
    /// <param name="bindings">The bindings to check</param>
    /// <returns>True if the bind group layout is a storage texture group, false otherwise</returns>
    public static bool IsStorageTextureGroup(Span<BindGroupEntry> bindings)
    {
        if (bindings.Length != 1)
        {
            return false;
        }

        return bindings[0].Type == BindingType.StorageTexture;
    }


}