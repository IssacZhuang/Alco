using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

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

    public ReadOnlySpan<GPUResourceGroup?> ResourceGroups
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _resourceGroups.Span;
    }

    internal ShaderParameterSet(ShaderReflectionInfo reflectionInfo)
    {
        _reflectionInfo = reflectionInfo;
        _slots = new ArrayBuffer<Slot>();
        _resourceGroups = new ArrayBuffer<GPUResourceGroup?>();

        UpdateSlotResources();
    }

    public void SetReflectionInfo(ShaderReflectionInfo reflectionInfo, bool resetResources = false)
    {
        _reflectionInfo = reflectionInfo;
        UpdateSlotResources(resetResources);
    }

    #region Set Buffer

    public bool TrySetBuffer(string name, GraphicsBuffer buffer)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySetBuffer(id, buffer);
    }


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

    public void SetBuffer(string name, GraphicsBuffer buffer)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetBuffer(id, buffer);
    }

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

    public bool TryGetBuffer(string name, [NotNullWhen(true)]out GraphicsBuffer? buffer)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            buffer = null;
            return false;
        }

        return TryGetBuffer(id, out buffer);
    }

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

    public GraphicsBuffer? GetBuffer(string name)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return null;
        }

        return GetBuffer(id);
    }

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

    public bool TrySetTexture(string name, Texture2D texture)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySetTexture(id, texture);
    }

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

    public Texture2D? GetTexture(string name)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return null;
        }

        return GetTexture(id);
    }

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

    public bool TrySetRenderTexture(string name, RenderTexture renderTexture, int index = 0)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySetRenderTexture(id, renderTexture, index);
    }

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

    public void SetRenderTexture(string name, RenderTexture renderTexture, int index = 0)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetRenderTexture(id, renderTexture, index);
    }

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

    public bool TrySetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySetRenderTextureDepth(id, renderTexture);
    }

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

    public void SetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetRenderTextureDepth(id, renderTexture);
    }

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

    public bool TryGetRenderTexture(string name, [NotNullWhen(true)]out RenderTexture? renderTexture)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            renderTexture = null;
            return false;
        }

        return TryGetRenderTexture(id, out renderTexture);
    }

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

    public RenderTexture? GetRenderTexture(string name)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            return null;
        }

        return GetRenderTexture(id);
    }

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
