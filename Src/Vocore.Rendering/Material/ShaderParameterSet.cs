using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;


/// <summary>
/// The shader parameter set which manages the resources of the shader.
/// </summary>
public sealed class ShaderParameterSet
{
    private const int RenderTextureIndexDepth = -1;
    private enum ResourceType
    {
        Unavailable,
        TextureWithSampler,
        TextureStorage,
        UniformBuffer,
        StorageBuffer
    }

    private struct Slot
    {
        public ResourceType type;
        public GraphicsBuffer? buffer;
        public Texture2D? texture;
        public RenderTexture? renderTexture;
        public int renderTextureIndex;
    }
    private readonly ArrayBuffer<Slot> _slots;
    private readonly ArrayBuffer<GPUResourceGroup?> _resourceGroups;
    private ShaderReflectionInfo _reflectionInfo;

    /// <summary>
    /// The resource groups of the shader.
    /// </summary>
    public ReadOnlySpan<GPUResourceGroup?> ResourceGroups
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _resourceGroups.Span;
    }

    /// <summary>
    /// Initialize the shader parameter set.
    /// </summary>
    /// <param name="reflectionInfo">The reflection information of the shader.</param>
    internal ShaderParameterSet(ShaderReflectionInfo reflectionInfo)
    {
        _reflectionInfo = reflectionInfo;
        _slots = new ArrayBuffer<Slot>();
        _resourceGroups = new ArrayBuffer<GPUResourceGroup?>();

        UpdateSlotResources();
    }

    /// <summary>
    /// Set the reflection information of the shader.
    /// </summary>
    /// <param name="reflectionInfo">The reflection information of the shader.</param>
    /// <param name="resetResources">Whether to reset the resources.</param>
    public void SetReflectionInfo(ShaderReflectionInfo reflectionInfo, bool resetResources = false)
    {
        _reflectionInfo = reflectionInfo;
        UpdateSlotResources(resetResources);
    }

    #region Set Buffer

    /// <summary>
    /// Try to set the buffer to the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="buffer">The buffer to set.</param>
    /// <returns>Whether the buffer is set successfully.</returns>
    public bool TrySetBuffer(string name, GraphicsBuffer buffer)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySetBuffer(id, buffer);
    }

    /// <summary>
    /// Try to set the buffer to the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="buffer">The buffer to set.</param>
    /// <returns>Whether the buffer is set successfully.</returns>
    public bool TrySetBuffer(uint id, GraphicsBuffer buffer)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return false;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type == ResourceType.UniformBuffer)
        {
            slot.buffer = buffer;
            _resourceGroups.Set(id, buffer.EntryReadonly);
            return true;
        }
        else if (slot.type == ResourceType.StorageBuffer)
        {
            slot.buffer = buffer;
            _resourceGroups.Set(id, buffer.EntryReadWrite);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Set the buffer to the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="buffer">The buffer to set.</param>
    public void SetBuffer(string name, GraphicsBuffer buffer)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetBuffer(id, buffer);
    }

    /// <summary>
    /// Set the buffer to the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="buffer">The buffer to set.</param>
    public void SetBuffer(uint id, GraphicsBuffer buffer)
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type == ResourceType.UniformBuffer)
        {
            slot.buffer = buffer;
            _resourceGroups.Set(id, buffer.EntryReadonly);
        }
        else if (slot.type == ResourceType.StorageBuffer)
        {
            slot.buffer = buffer;
            _resourceGroups.Set(id, buffer.EntryReadWrite);
        }
        else
        {
            throw new InvalidOperationException($"The bind group {id} is not for a buffer but {slot.type}.");
        }


    }

    #endregion

    #region Get Buffer

    /// <summary>
    /// Try to get the buffer from the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="buffer">The buffer to get.</param>
    /// <returns>Whether the buffer is got successfully.</returns>
    public bool TryGetBuffer(string name, [NotNullWhen(true)]out GraphicsBuffer? buffer)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            buffer = null;
            return false;
        }

        return TryGetBuffer(id, out buffer);
    }

    /// <summary>
    /// Try to get the buffer from the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="buffer">The buffer to get.</param>
    /// <returns>Whether the buffer is got successfully.</returns>
    public bool TryGetBuffer(uint id, [NotNullWhen(true)]out GraphicsBuffer? buffer)
    {
        if (id < 0 || id >= _slots.Length)
        {
            buffer = null;
            return false;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type == ResourceType.UniformBuffer || slot.type == ResourceType.StorageBuffer)
        {
            buffer = slot.buffer;
            return buffer != null;
        }

        buffer = null;
        return false;
    }

    /// <summary>
    /// Get the buffer from the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <returns>The buffer.</returns>
    public GraphicsBuffer? GetBuffer(string name)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return null;
        }

        return GetBuffer(id);
    }

    /// <summary>
    /// Get the buffer from the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <returns>The buffer.</returns>
    public GraphicsBuffer? GetBuffer(uint id)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return null;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type == ResourceType.UniformBuffer || slot.type == ResourceType.StorageBuffer)
        {
            return slot.buffer;
        }

        return null;
    }

    #endregion


    #region Set Texture

    /// <summary>
    /// Try to set the texture to the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="texture">The texture to set.</param>
    /// <returns>Whether the texture is set successfully.</returns>
    public bool TrySetTexture(string name, Texture2D texture)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySetTexture(id, texture);
    }

    /// <summary>
    /// Try to set the texture to the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="texture">The texture to set.</param>
    /// <returns>Whether the texture is set successfully.</returns>
    public bool TrySetTexture(uint id, Texture2D texture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return false;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type == ResourceType.TextureWithSampler)
        {
            slot.texture = texture;
            _resourceGroups.Set(id, texture.EntrySample);
            return true;
        }
        else if (slot.type == ResourceType.TextureStorage)
        {
            slot.texture = texture;
            _resourceGroups.Set(id, texture.EntryWriteable);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Set the texture to the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="texture">The texture to set.</param>
    public void SetTexture(string name, Texture2D texture)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetTexture(id, texture);
    }

    public void SetTexture(uint id, Texture2D texture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type == ResourceType.TextureWithSampler)
        {
            slot.texture = texture;
            _resourceGroups.Set(id, texture.EntrySample);
        }
        else if (slot.type == ResourceType.TextureStorage)
        {
            slot.texture = texture;
            _resourceGroups.Set(id, texture.EntryWriteable);
        }
        else
        {
            throw new InvalidOperationException($"The bind group {id} is not for a texture but {slot.type}.");
        }
    }

    #endregion

    #region Get Texture

    /// <summary>
    /// Try to get the texture from the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="texture">The texture to get.</param>
    /// <returns>Whether the texture is got successfully.</returns>
    public bool TryGetTexture(string name, [NotNullWhen(true)]out Texture2D? texture)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            texture = null;
            return false;
        }

        return TryGetTexture(id, out texture);
    }

    public bool TryGetTexture(uint id, [NotNullWhen(true)]out Texture2D? texture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            texture = null;
            return false;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type == ResourceType.TextureWithSampler || slot.type == ResourceType.TextureStorage)
        {
            texture = slot.texture;
            return texture != null;
        }

        texture = null;
        return false;
    }

    /// <summary>
    /// Get the texture from the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <returns>The texture.</returns>
    public Texture2D? GetTexture(string name)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return null;
        }

        return GetTexture(id);
    }

    /// <summary>
    /// Get the texture from the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <returns>The texture.</returns>
    public Texture2D? GetTexture(uint id)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return null;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type == ResourceType.TextureWithSampler || slot.type == ResourceType.TextureStorage)
        {
            return slot.texture;
        }

        return null;
    }

    #endregion

    #region Set RenderTexture Color

    /// <summary>
    /// Try to set the color texture in the render texture to the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="index">The index of the color texture in the render texture.</param>
    /// <returns>Whether the color texture in the render texture is set successfully.</returns>
    public bool TrySetRenderTexture(string name, RenderTexture renderTexture, int index = 0)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySetRenderTexture(id, renderTexture, index);
    }

    /// <summary>
    /// Try to set the color texture in the render texture to the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="index">The index of the color texture in the render texture.</param>
    /// <returns>Whether the color texture in the render texture is set successfully.</returns> 
    public bool TrySetRenderTexture(uint id, RenderTexture renderTexture, int index = 0)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return false;
        }

        if (index < 0 || index >= renderTexture.EntriesColorSample.Length)
        {
            return false;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if(slot.type == ResourceType.TextureWithSampler)
        {
            slot.texture = null;
            slot.renderTexture = renderTexture;
            slot.renderTextureIndex = index;
            _resourceGroups.Set(id, renderTexture.EntriesColorSample[index]);
            return true;
        }
        else if (slot.type == ResourceType.TextureStorage)
        {
            slot.texture = null;
            slot.renderTexture = renderTexture;
            slot.renderTextureIndex = index;
            _resourceGroups.Set(id, renderTexture.EntriesColorWrite[index]);
            return true;
        }

        return false;
    }
  
    /// <summary>
    /// Set the color texture in the render texture to the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="index">The index of the color texture in the render texture.</param>
    public void SetRenderTexture(string name, RenderTexture renderTexture, int index = 0)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetRenderTexture(id, renderTexture, index);
    }

    /// <summary>
    /// Set the color texture in the render texture to the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="index">The index of the color texture in the render texture.</param>
    public void SetRenderTexture(uint id, RenderTexture renderTexture, int index = 0)
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        if (index < 0 || index >= renderTexture.EntriesColorSample.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "The render texture index is out of range.");
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type == ResourceType.TextureWithSampler)
        {
            slot.texture = null;
            slot.renderTexture = renderTexture;
            slot.renderTextureIndex = index;
            _resourceGroups.Set(id, renderTexture.EntriesColorSample[index]);
        }
        else if (slot.type == ResourceType.TextureStorage)
        {
            slot.texture = null;
            slot.renderTexture = renderTexture;
            slot.renderTextureIndex = index;
            _resourceGroups.Set(id, renderTexture.EntriesColorWrite[index]);
        }else{
            throw new InvalidOperationException($"The bind group {id} is not for a texture but {slot.type}.");
        }
    }

    #endregion

    #region Set RenderTexture Depth

    /// <summary>
    /// Try to set the depth texture in the render texture to the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <returns>Whether the depth texture in the render texture is set successfully.</returns>
    public bool TrySetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySetRenderTextureDepth(id, renderTexture);
    }

    /// <summary>
    /// Try to set the depth texture in the render texture to the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <returns>Whether the depth texture in the render texture is set successfully.</returns>
    public bool TrySetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return false;
        }

        if (!renderTexture.HasDepth)
        {
            return false;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.TextureWithSampler)
        {
            return false;
        }

        // The depth texture is not supported for writeable texture
        slot.texture = null;
        slot.renderTexture = renderTexture;
        slot.renderTextureIndex = RenderTextureIndexDepth;
        _resourceGroups.Set(id, renderTexture.EntryDepthSample);

        return true;
    }

    /// <summary>
    /// Set the depth texture in the render texture to the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    public void SetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetRenderTextureDepth(id, renderTexture);
    }

    /// <summary>
    /// Set the depth texture in the render texture to the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    public void SetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        if (!renderTexture.HasDepth)
        {
            throw new InvalidOperationException("The render texture does not have a depth attachment.");
        }

        ref Slot slot = ref _slots.GetRef(id);

        if (slot.type != ResourceType.TextureWithSampler)
        {
            throw new InvalidOperationException($"The depth texture only supports texture with sampler which is not the case for bind group {id}.");
        }

        slot.texture = null;
        slot.renderTexture = renderTexture;
        slot.renderTextureIndex = RenderTextureIndexDepth;
        _resourceGroups.Set(id, renderTexture.EntryDepthSample);
    }

    #endregion

    #region Get RenderTexture

    /// <summary>
    /// Try to get the render texture from the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="renderTexture">The render texture to get.</param>
    /// <returns>Whether the render texture is got successfully.</returns>
    public bool TryGetRenderTexture(string name, [NotNullWhen(true)]out RenderTexture? renderTexture)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            renderTexture = null;
            return false;
        }

        return TryGetRenderTexture(id, out renderTexture);
    }

    /// <summary>
    /// Try to get the render texture from the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="renderTexture">The render texture to get.</param>
    /// <returns>Whether the render texture is got successfully.</returns>
    public bool TryGetRenderTexture(uint id, [NotNullWhen(true)]out RenderTexture? renderTexture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            renderTexture = null;
            return false;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type == ResourceType.TextureWithSampler || slot.type == ResourceType.TextureStorage)
        {
            renderTexture = slot.renderTexture;
            return renderTexture != null;
        }

        renderTexture = null;
        return false;
    }

    /// <summary>
    /// Get the render texture from the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <returns>The render texture.</returns>
    public RenderTexture? GetRenderTexture(string name)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return null;
        }

        return GetRenderTexture(id);
    }

    /// <summary>
    /// Get the render texture from the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <returns>The render texture.</returns>
    public RenderTexture? GetRenderTexture(uint id)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return null;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type == ResourceType.TextureWithSampler || slot.type == ResourceType.TextureStorage)
        {
            return slot.renderTexture;
        }

        return null;
    }


    #endregion

    private void UpdateSlotResources(bool reset = false)
    {
        ShaderReflectionInfo reflectionInfo = _reflectionInfo;
        int slotCount = reflectionInfo.BindGroups.Count;

        _slots.EnsureSize(slotCount);
        _resourceGroups.EnsureSize(slotCount);

        for (int i = 0; i < reflectionInfo.BindGroups.Count; i++)
        {
            ref Slot slot = ref _slots[i];
            BindGroupLayout bindGroupLayout = reflectionInfo.BindGroups[i];
            if (UtilsMaterial.IsUniformBufferGroup(bindGroupLayout.Bindings))
            {
                slot.type = ResourceType.UniformBuffer;
            }
            else if (UtilsMaterial.IsStorageBufferGroup(bindGroupLayout.Bindings))
            {
                slot.type = ResourceType.StorageBuffer;
            }
            else if (UtilsMaterial.IsStorageTextureGroup(bindGroupLayout.Bindings))
            {
                slot.type = ResourceType.TextureStorage;
            }
            else if (UtilsMaterial.IsTextureSamplerGroup(bindGroupLayout.Bindings))
            {
                slot.type = ResourceType.TextureWithSampler;
            }
            else
            {
                slot.type = ResourceType.Unavailable;
            }
        }

        if (reset)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot slot = ref _slots.GetRef(i);
                slot.buffer = null;
                slot.texture = null;
                slot.renderTexture = null;
                slot.renderTextureIndex = 0;
            }

            for (int i = 0; i < _resourceGroups.Length; i++)
            {
                _resourceGroups[i] = null;
            }
        }
    }
}
