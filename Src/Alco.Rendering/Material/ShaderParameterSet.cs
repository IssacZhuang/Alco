using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;


/// <summary>
/// The shader parameter set which manages the resources of the shader.
/// <br/>Resources are addressed by dense resource id (one slot per settable shader
/// variable: a buffer or a texture). The bind groups of the shader are assembled
/// lazily from the slot values: a group is (re)built only when one of its values
/// changed, and identical contents are served from a per-group cache, so ping-pong
/// updates (e.g. double buffering) do not recreate bind groups every frame.
/// </summary>
public sealed class ShaderParameterSet
{
    private const int RenderTextureIndexDepth = -1;
    // The number of distinct contents kept per bind group. Evicted entries are only
    // dropped from the cache, never disposed: recorded render bundles may still
    // reference them.
    private const int MaxCachedGroups = 16;

    private enum ResourceType
    {
        Unavailable,
        TextureWithSampler,
        TextureRead,
        TextureStorage,
        UniformBuffer,
        StorageBuffer,
        StorageBufferWithCounter
    }

    private struct Slot
    {
        public ResourceType type;
        public int groupIndex;
        public bool isDepth;
        public GraphicsBuffer? buffer;
        public Texture? texture;
        public RenderTexture? renderTexture;
        public int renderTextureIndex;
        public uint mipLevel;
        public bool mipView;
    }

    private enum EntryKind : byte
    {
        Resource,
        OwnerSampler,
        OwnerCounter
    }

    // How to fill one binding of a bind group during assembly.
    private struct EntryPlan
    {
        public uint binding;
        public BindingType entryType;
        public EntryKind kind;
        public int slotIndex;
    }

    private sealed class GroupState
    {
        public GPUBindGroup? layout;
        public EntryPlan[] plans = [];
        public bool dirty = true;
        public GPUResourceGroup? current;
        public readonly Dictionary<ulong, GPUResourceGroup> cache = new();
        public readonly Queue<ulong> cacheOrder = new();
    }

    private readonly GPUDevice _device;
    private ShaderReflectionInfo _reflectionInfo;
    private Slot[] _slots;
    private GroupState[] _groups;
    private GPUResourceGroup?[] _resourceGroups;
    // The parameter set to resolve unbound slot values from (material instance parenting).
    private ShaderParameterSet? _fallback;

    /// <summary>
    /// Get the reflection information of the shader.
    /// </summary>
    public ShaderReflectionInfo ReflectionInfo => _reflectionInfo;

    /// <summary>
    /// The parameter set used to resolve values for slots that have no value of their
    /// own (material instance parenting). When a fallback is set,
    /// <see cref="FlushResourceGroups"/> always re-resolves the groups, since changes
    /// of the fallback values are not tracked here.
    /// </summary>
    internal ShaderParameterSet? Fallback
    {
        get => _fallback;
        set => _fallback = value;
    }

    /// <summary>
    /// The assembled resource groups of the shader, indexed by bind group.
    /// Call <see cref="FlushResourceGroups"/> first to pick up slot changes.
    /// </summary>
    public ReadOnlySpan<GPUResourceGroup?> ResourceGroups
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _resourceGroups.AsSpan();
    }

    /// <summary>
    /// Initialize the shader parameter set.
    /// </summary>
    /// <param name="device">The GPU device used to assemble the bind groups.</param>
    /// <param name="reflectionInfo">The reflection information of the shader.</param>
    internal ShaderParameterSet(GPUDevice device, ShaderReflectionInfo reflectionInfo)
    {
        _device = device;
        _reflectionInfo = reflectionInfo;
        _slots = [];
        _groups = [];
        _resourceGroups = [];

        BuildSlotsAndGroups();
    }

    /// <summary>
    /// Set the reflection information of the shader.
    /// Slot values are carried over by resource name when the shader is recompiled.
    /// </summary>
    /// <param name="reflectionInfo">The reflection information of the shader.</param>
    /// <param name="resetResources">Whether to reset the resources.</param>
    public void SetReflectionInfo(ShaderReflectionInfo reflectionInfo, bool resetResources = false)
    {
        ShaderReflectionInfo oldReflection = _reflectionInfo;
        Slot[] oldSlots = _slots;

        _reflectionInfo = reflectionInfo;
        BuildSlotsAndGroups();

        if (resetResources)
        {
            return;
        }

        // Carry over the values of the resources that survived the recompile,
        // identified by name so a regrouping or reordering does not lose them.
        IReadOnlyList<ShaderResourceLocation> oldLocations = oldReflection.ResourceLocations;
        IReadOnlyList<ShaderResourceLocation> newLocations = reflectionInfo.ResourceLocations;
        Dictionary<string, int> oldByName = new Dictionary<string, int>();
        for (int i = 0; i < oldLocations.Count; i++)
        {
            oldByName[oldLocations[i].Name] = i;
        }

        for (int i = 0; i < newLocations.Count; i++)
        {
            if (!oldByName.TryGetValue(newLocations[i].Name, out int oldIndex))
            {
                continue;
            }

            ref Slot newSlot = ref _slots[i];
            ref Slot oldSlot = ref oldSlots[oldIndex];
            if (newSlot.type != oldSlot.type)
            {
                continue;
            }

            if (oldSlot.buffer == null && oldSlot.texture == null && oldSlot.renderTexture == null)
            {
                continue;
            }

            newSlot.buffer = oldSlot.buffer;
            newSlot.texture = oldSlot.texture;
            newSlot.renderTexture = oldSlot.renderTexture;
            newSlot.renderTextureIndex = oldSlot.renderTextureIndex;
            newSlot.mipLevel = oldSlot.mipLevel;
            newSlot.mipView = oldSlot.mipView;
        }
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
        if (id >= (uint)_slots.Length)
        {
            return false;
        }

        ref Slot slot = ref _slots[id];
        if (slot.type != ResourceType.UniformBuffer
        && slot.type != ResourceType.StorageBuffer
        && slot.type != ResourceType.StorageBufferWithCounter)
        {
            return false;
        }

        if (ReferenceEquals(slot.buffer, buffer))
        {
            return true;
        }

        slot.buffer = buffer;
        MarkDirty(slot.groupIndex);
        return true;
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
        if (id >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        ref Slot slot = ref _slots[id];
        if (slot.type != ResourceType.UniformBuffer
        && slot.type != ResourceType.StorageBuffer
        && slot.type != ResourceType.StorageBufferWithCounter)
        {
            throw new InvalidOperationException($"The resource {id}({_reflectionInfo.GetResourceName(id)}) is not for a buffer but {slot.type}.");
        }

        if (ReferenceEquals(slot.buffer, buffer))
        {
            return;
        }

        slot.buffer = buffer;
        MarkDirty(slot.groupIndex);
    }

    #endregion

    #region Get Buffer

    /// <summary>
    /// Try to get the buffer from the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="buffer">The buffer to get.</param>
    /// <returns>Whether the buffer is got successfully.</returns>
    public bool TryGetBuffer(string name, [NotNullWhen(true)] out GraphicsBuffer? buffer)
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
    public bool TryGetBuffer(uint id, [NotNullWhen(true)] out GraphicsBuffer? buffer)
    {
        if (id >= (uint)_slots.Length)
        {
            buffer = null;
            return false;
        }

        ref Slot slot = ref _slots[id];
        if (slot.type == ResourceType.UniformBuffer || slot.type == ResourceType.StorageBuffer || slot.type == ResourceType.StorageBufferWithCounter)
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
        if (id >= (uint)_slots.Length)
        {
            return null;
        }

        ref Slot slot = ref _slots[id];
        if (slot.type == ResourceType.UniformBuffer || slot.type == ResourceType.StorageBuffer || slot.type == ResourceType.StorageBufferWithCounter)
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
    public bool TrySetTexture(string name, Texture texture)
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
    public bool TrySetTexture(uint id, Texture texture)
    {
        if (id >= (uint)_slots.Length)
        {
            return false;
        }

        ref Slot slot = ref _slots[id];
        if (!IsTextureSlot(slot.type))
        {
            return false;
        }

        // A 3D texture bound to a storage slot writes the mip level 0 view by default.
        bool mipView = slot.type == ResourceType.TextureStorage && texture is Texture3D;
        SetTextureValue(ref slot, texture, null, 0, 0, mipView);
        return true;
    }

    /// <summary>
    /// Set the texture to the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="texture">The texture to set.</param>
    public void SetTexture(string name, Texture texture)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetTexture(id, texture);
    }

    /// <summary>
    /// Set the texture to the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="texture">The texture to set.</param>
    public void SetTexture(uint id, Texture texture)
    {
        if (id >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        ref Slot slot = ref _slots[id];
        if (!IsTextureSlot(slot.type))
        {
            throw new InvalidOperationException($"The resource {id}({_reflectionInfo.GetResourceName(id)}) is not for a texture but {slot.type}.");
        }

        bool mipView = slot.type == ResourceType.TextureStorage && texture is Texture3D;
        SetTextureValue(ref slot, texture, null, 0, 0, mipView);
    }

    /// <summary>
    /// Set the storage resource group of a single mip level of a 3D texture to a
    /// storage texture slot.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="texture">The 3D texture to bind.</param>
    /// <param name="mipLevel">The mip level to write (0 = full resolution).</param>
    public void SetTexture3DStorage(string name, Texture3D texture, uint mipLevel)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetTexture3DStorage(id, texture, mipLevel);
    }

    /// <summary>
    /// Set the storage resource group of a single mip level of a 3D texture to a
    /// storage texture slot.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="texture">The 3D texture to bind.</param>
    /// <param name="mipLevel">The mip level to write (0 = full resolution).</param>
    /// <exception cref="ArgumentOutOfRangeException">The resource ID is out of range.</exception>
    /// <exception cref="InvalidOperationException">The slot is not a storage texture slot.</exception>
    public void SetTexture3DStorage(uint id, Texture3D texture, uint mipLevel)
    {
        if (id >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        ref Slot slot = ref _slots[id];
        if (slot.type != ResourceType.TextureStorage)
        {
            throw new InvalidOperationException($"The resource {id}({_reflectionInfo.GetResourceName(id)}) is not for a storage texture but {slot.type}.");
        }

        SetTextureValue(ref slot, texture, null, 0, mipLevel, true);
    }

    /// <summary>
    /// Set the read-only resource group of a single mip level of a 3D texture to a
    /// read-only texture slot. Inside the bound view the mip is rebased to mip 0, so
    /// shaders load it with mip index 0.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="texture">The 3D texture to bind.</param>
    /// <param name="mipLevel">The mip level to read (0 = full resolution).</param>
    public void SetTexture3DRead(string name, Texture3D texture, uint mipLevel)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetTexture3DRead(id, texture, mipLevel);
    }

    /// <summary>
    /// Set the read-only resource group of a single mip level of a 3D texture to a
    /// read-only texture slot. Inside the bound view the mip is rebased to mip 0, so
    /// shaders load it with mip index 0.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="texture">The 3D texture to bind.</param>
    /// <param name="mipLevel">The mip level to read (0 = full resolution).</param>
    /// <exception cref="ArgumentOutOfRangeException">The resource ID is out of range.</exception>
    /// <exception cref="InvalidOperationException">The slot is not a read-only texture slot.</exception>
    public void SetTexture3DRead(uint id, Texture3D texture, uint mipLevel)
    {
        if (id >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        ref Slot slot = ref _slots[id];
        if (slot.type != ResourceType.TextureRead)
        {
            throw new InvalidOperationException($"The resource {id}({_reflectionInfo.GetResourceName(id)}) is not for a read-only texture but {slot.type}.");
        }

        SetTextureValue(ref slot, texture, null, 0, mipLevel, true);
    }

    #endregion

    #region Get Texture

    /// <summary>
    /// Try to get the texture from the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="texture">The texture to get.</param>
    /// <returns>Whether the texture is got successfully.</returns>
    public bool TryGetTexture(string name, [NotNullWhen(true)] out Texture2D? texture)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            texture = null;
            return false;
        }

        return TryGetTexture(id, out texture);
    }

    /// <summary>
    /// Try to get the texture from the shader parameter set.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="texture">The texture to get.</param>
    /// <returns>Whether the texture is got successfully.</returns>
    public bool TryGetTexture(uint id, [NotNullWhen(true)] out Texture2D? texture)
    {
        if (id >= (uint)_slots.Length)
        {
            texture = null;
            return false;
        }

        ref Slot slot = ref _slots[id];
        if (IsTextureSlot(slot.type))
        {
            texture = slot.texture as Texture2D;
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
        if (id >= (uint)_slots.Length)
        {
            return null;
        }

        ref Slot slot = ref _slots[id];
        if (IsTextureSlot(slot.type))
        {
            return slot.texture as Texture2D;
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
        if (id >= (uint)_slots.Length)
        {
            return false;
        }

        if (index < 0 || index >= renderTexture.ColorTextures.Length)
        {
            return false;
        }

        ref Slot slot = ref _slots[id];
        if (!IsTextureSlot(slot.type))
        {
            return false;
        }

        SetTextureValue(ref slot, null, renderTexture, index, 0, false);
        return true;
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
        if (id >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        if (index < 0 || index >= renderTexture.ColorTextures.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "The render texture index is out of range.");
        }

        ref Slot slot = ref _slots[id];
        if (!IsTextureSlot(slot.type))
        {
            throw new InvalidOperationException($"The resource {id}({_reflectionInfo.GetResourceName(id)}) is not for a texture but {slot.type}.");
        }

        SetTextureValue(ref slot, null, renderTexture, index, 0, false);
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
        if (id >= (uint)_slots.Length)
        {
            return false;
        }

        if (!renderTexture.HasDepth)
        {
            return false;
        }

        ref Slot slot = ref _slots[id];
        if (!slot.isDepth)
        {
            return false;
        }

        if (slot.type != ResourceType.TextureRead && slot.type != ResourceType.TextureWithSampler)
        {
            return false;
        }

        SetTextureValue(ref slot, null, renderTexture, RenderTextureIndexDepth, 0, false);
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
    /// The target resource must be a depth texture (a texture declared with the
    /// DEFINE_TEX2D_DEPTH or DEFINE_TEX2D_DEPTH_SAMPLE macro); a texture-only
    /// resource gets the depth view, a texture-and-comparison-sampler resource
    /// additionally gets the device default comparison sampler.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    public void SetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        if (id >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        if (!renderTexture.HasDepth)
        {
            throw new InvalidOperationException("The render texture does not have a depth attachment.");
        }

        ref Slot slot = ref _slots[id];
        if (!slot.isDepth)
        {
            throw new InvalidOperationException($"The resource {id}({_reflectionInfo.GetResourceName(id)}) is not a depth texture. Declare the texture with the DEFINE_TEX2D_DEPTH or DEFINE_TEX2D_DEPTH_SAMPLE macro.");
        }

        if (slot.type != ResourceType.TextureRead && slot.type != ResourceType.TextureWithSampler)
        {
            throw new InvalidOperationException($"The depth texture only supports texture read or texture with sampler which is not the case for resource {id}({_reflectionInfo.GetResourceName(id)}).");
        }

        SetTextureValue(ref slot, null, renderTexture, RenderTextureIndexDepth, 0, false);
    }

    #endregion

    #region Get RenderTexture

    /// <summary>
    /// Try to get the render texture from the shader parameter set.
    /// </summary>
    /// <param name="name">The shader resource name of the resource.</param>
    /// <param name="renderTexture">The render texture to get.</param>
    /// <returns>Whether the render texture is got successfully.</returns>
    public bool TryGetRenderTexture(string name, [NotNullWhen(true)] out RenderTexture? renderTexture)
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
    public bool TryGetRenderTexture(uint id, [NotNullWhen(true)] out RenderTexture? renderTexture)
    {
        if (id >= (uint)_slots.Length)
        {
            renderTexture = null;
            return false;
        }

        ref Slot slot = ref _slots[id];
        if (IsTextureSlot(slot.type))
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
        if (id >= (uint)_slots.Length)
        {
            return null;
        }

        ref Slot slot = ref _slots[id];
        if (IsTextureSlot(slot.type))
        {
            return slot.renderTexture;
        }

        return null;
    }


    #endregion

    #region Resource Group Assembly

    /// <summary>
    /// Rebuilds the assembled resource groups whose slot values changed since the
    /// last call. Identical contents are served from a per-group cache, so repeated
    /// updates (e.g. double buffered ping-pong) do not recreate bind groups.
    /// </summary>
    public void FlushResourceGroups()
    {
        // With a fallback set, changes of the fallback values are not tracked, so
        // every group is re-resolved; the content cache still prevents rebuilds.
        bool force = _fallback != null;
        for (int i = 0; i < _groups.Length; i++)
        {
            GroupState group = _groups[i];
            if (!force && !group.dirty)
            {
                continue;
            }

            group.dirty = false;
            group.current = AssembleGroup(i, group);
            _resourceGroups[i] = group.current;
        }
    }

    /// <summary>
    /// Whether the resource with the given id is a sampled texture slot (texture and
    /// sampler, not a depth texture) that has no value yet and therefore accepts the
    /// default texture.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <returns>True if the slot accepts the default texture.</returns>
    internal bool NeedsDefaultTexture(uint id)
    {
        if (id >= (uint)_slots.Length)
        {
            return false;
        }

        ref Slot slot = ref _slots[id];
        return slot.type == ResourceType.TextureWithSampler
            && !slot.isDepth
            && slot.texture == null
            && slot.renderTexture == null;
    }

    private GPUResourceGroup? AssembleGroup(int groupIndex, GroupState group)
    {
        EntryPlan[] plans = group.plans;
        IGPUBindableResource?[] values = new IGPUBindableResource?[plans.Length];

        // FNV-1a over the ordered (binding, resource identity) pairs.
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < plans.Length; i++)
        {
            IGPUBindableResource? value = ResolveEntryValue(in plans[i]);
            if (value == null)
            {
                // The group is not fully bound yet.
                return null;
            }

            values[i] = value;
            hash = (hash ^ plans[i].binding) * 1099511628211UL;
            hash = (hash ^ (ulong)RuntimeHelpers.GetHashCode(value)) * 1099511628211UL;
        }

        if (group.cache.TryGetValue(hash, out GPUResourceGroup? cached))
        {
            return cached;
        }

        group.layout ??= _device.CreateBindGroup(_reflectionInfo.BindGroups[groupIndex].ToDescriptor($"material_bind_group_layout_{groupIndex}"));

        ResourceBindingEntry[] resources = new ResourceBindingEntry[plans.Length];
        for (int i = 0; i < plans.Length; i++)
        {
            resources[i] = new ResourceBindingEntry(plans[i].binding, values[i]!);
        }

        GPUResourceGroup resourceGroup = _device.CreateResourceGroup(new ResourceGroupDescriptor(group.layout, resources, $"material_bind_group_{groupIndex}"));

        while (group.cacheOrder.Count >= MaxCachedGroups)
        {
            ulong evicted = group.cacheOrder.Dequeue();
            group.cache.Remove(evicted);
        }

        group.cache[hash] = resourceGroup;
        group.cacheOrder.Enqueue(hash);
        return resourceGroup;
    }

    private IGPUBindableResource? ResolveEntryValue(in EntryPlan plan)
    {
        IGPUBindableResource? value = ResolveOwnSlot(plan.slotIndex, plan.kind, plan.entryType);
        if (value != null)
        {
            return value;
        }

        // The fallback chain is resolved by resource name rather than by slot index,
        // so a parent compiled with different defines (a different resource layout)
        // still provides the values the child did not bind.
        string name = _reflectionInfo.ResourceLocations[plan.slotIndex].Name;
        for (ShaderParameterSet? set = _fallback; set != null; set = set._fallback)
        {
            if (set._reflectionInfo.TryGetResourceId(name, out uint fallbackId))
            {
                value = set.ResolveOwnSlot((int)fallbackId, plan.kind, plan.entryType);
                if (value != null)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private IGPUBindableResource? ResolveOwnSlot(int slotIndex, EntryKind kind, BindingType entryType)
    {
        ref Slot slot = ref _slots[slotIndex];
        switch (kind)
        {
            case EntryKind.Resource:
                switch (slot.type)
                {
                    case ResourceType.UniformBuffer:
                    case ResourceType.StorageBuffer:
                    case ResourceType.StorageBufferWithCounter:
                        return slot.buffer?.NativeBuffer;
                    case ResourceType.TextureWithSampler:
                    case ResourceType.TextureRead:
                    case ResourceType.TextureStorage:
                        return ResolveView(in slot);
                    default:
                        return null;
                }
            case EntryKind.OwnerSampler:
                if (entryType == BindingType.SamplerComparison)
                {
                    return _device.SamplerDepthComparison;
                }

                return ResolveSampler(in slot);
            case EntryKind.OwnerCounter:
                return slot.buffer?.CounterBuffer;
            default:
                return null;
        }
    }

    private GPUTextureView? ResolveView(in Slot slot)
    {
        if (slot.renderTexture != null)
        {
            if (slot.renderTextureIndex == RenderTextureIndexDepth)
            {
                return slot.renderTexture.DepthView;
            }

            return slot.renderTexture.ColorTextures[slot.renderTextureIndex].View;
        }

        Texture? texture = slot.texture;
        if (texture == null)
        {
            return null;
        }

        if (slot.mipView && texture is Texture3D texture3D)
        {
            return texture3D.GetMipView(slot.mipLevel);
        }

        return texture.View;
    }

    private GPUSampler? ResolveSampler(in Slot slot)
    {
        if (slot.renderTexture != null)
        {
            if (slot.renderTextureIndex == RenderTextureIndexDepth)
            {
                return _device.SamplerDepthComparison;
            }

            return slot.renderTexture.ColorTextures[slot.renderTextureIndex].Sampler;
        }

        return slot.texture?.Sampler;
    }

    private void SetTextureValue(ref Slot slot, Texture? texture, RenderTexture? renderTexture, int renderTextureIndex, uint mipLevel, bool mipView)
    {
        if (ReferenceEquals(slot.texture, texture)
        && ReferenceEquals(slot.renderTexture, renderTexture)
        && slot.renderTextureIndex == renderTextureIndex
        && slot.mipLevel == mipLevel
        && slot.mipView == mipView)
        {
            return;
        }

        slot.texture = texture;
        slot.renderTexture = renderTexture;
        slot.renderTextureIndex = renderTextureIndex;
        slot.mipLevel = mipLevel;
        slot.mipView = mipView;
        MarkDirty(slot.groupIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkDirty(int groupIndex)
    {
        _groups[groupIndex].dirty = true;
    }

    private static bool IsTextureSlot(ResourceType type)
    {
        return type == ResourceType.TextureWithSampler
            || type == ResourceType.TextureRead
            || type == ResourceType.TextureStorage;
    }

    #endregion

    private void BuildSlotsAndGroups()
    {
        ShaderReflectionInfo reflection = _reflectionInfo;
        int groupCount = reflection.BindGroups.Count;

        for (int i = 0; i < _groups.Length; i++)
        {
            _groups[i].layout?.Dispose();
        }

        IReadOnlyList<ShaderResourceLocation> locations = reflection.ResourceLocations;
        _slots = new Slot[locations.Count];
        _groups = new GroupState[groupCount];
        _resourceGroups = new GPUResourceGroup?[groupCount];

        // Pass 1: one slot per settable resource.
        for (int i = 0; i < locations.Count; i++)
        {
            ShaderResourceLocation location = locations[i];
            BindGroupEntry entry = reflection.BindGroups[location.GroupIndex].Bindings[location.EntryIndex].Entry;
            ref Slot slot = ref _slots[i];
            slot.groupIndex = location.GroupIndex;
            slot.isDepth = entry.Type == BindingType.Texture && entry.TextureInfo.SampleType == TextureSampleType.Depth;
            slot.type = entry.Type switch
            {
                BindingType.UniformBuffer => ResourceType.UniformBuffer,
                BindingType.StorageBuffer => ResourceType.StorageBuffer,
                // Upgraded to TextureWithSampler in pass 2 when a sampler companion exists.
                BindingType.Texture => ResourceType.TextureRead,
                BindingType.StorageTexture => ResourceType.TextureStorage,
                _ => ResourceType.Unavailable,
            };
        }

        // Pass 2: per-group entry plans; resolve the sampler and counter companions
        // to the resource that owns them.
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            IReadOnlyList<BindGroupEntryInfo> bindings = reflection.BindGroups[groupIndex].Bindings;
            EntryPlan[] plans = new EntryPlan[bindings.Count];
            for (int entryIndex = 0; entryIndex < bindings.Count; entryIndex++)
            {
                BindGroupEntry entry = bindings[entryIndex].Entry;
                plans[entryIndex].binding = entry.Binding;
                plans[entryIndex].entryType = entry.Type;

                switch (entry.Type)
                {
                    case BindingType.Sampler:
                    case BindingType.SamplerComparison:
                    {
                        string? samplerOwner = entry.Name.EndsWith(ShaderReflectionInfo.SamplerSuffix, StringComparison.Ordinal)
                            ? entry.Name.Substring(0, entry.Name.Length - ShaderReflectionInfo.SamplerSuffix.Length)
                            : null;
                        int owner = FindOwnerSlot(locations, groupIndex, entry, BindingType.Texture, samplerOwner);
                        plans[entryIndex].kind = EntryKind.OwnerSampler;
                        plans[entryIndex].slotIndex = owner;
                        ref Slot ownerSlot = ref _slots[owner];
                        if (ownerSlot.type == ResourceType.TextureRead)
                        {
                            ownerSlot.type = ResourceType.TextureWithSampler;
                        }

                        break;
                    }
                    case BindingType.StorageBuffer when ShaderReflectionInfo.IsCounterCompanion(entry, out string? counterOwner):
                    {
                        int owner = FindOwnerSlot(locations, groupIndex, entry, BindingType.StorageBuffer, counterOwner);
                        plans[entryIndex].kind = EntryKind.OwnerCounter;
                        plans[entryIndex].slotIndex = owner;
                        ref Slot ownerSlot = ref _slots[owner];
                        if (ownerSlot.type == ResourceType.StorageBuffer)
                        {
                            ownerSlot.type = ResourceType.StorageBufferWithCounter;
                        }

                        break;
                    }
                    default:
                    {
                        plans[entryIndex].kind = EntryKind.Resource;
                        plans[entryIndex].slotIndex = FindResourceSlot(locations, groupIndex, entryIndex);
                        break;
                    }
                }
            }

            _groups[groupIndex] = new GroupState { plans = plans, dirty = true };
        }
    }

    private static int FindResourceSlot(IReadOnlyList<ShaderResourceLocation> locations, int groupIndex, int entryIndex)
    {
        for (int i = 0; i < locations.Count; i++)
        {
            if (locations[i].GroupIndex == groupIndex && locations[i].EntryIndex == entryIndex)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"The entry at binding {entryIndex} of bind group {groupIndex} is not a settable resource.");
    }

    // A companion entry (sampler or counter) belongs to the resource of the given
    // type in the same bind group with the given owner name; as a fallback the
    // resource at the previous binding is used.
    private static int FindOwnerSlot(IReadOnlyList<ShaderResourceLocation> locations, int groupIndex, in BindGroupEntry entry, BindingType ownerType, string? ownerName)
    {
        if (ownerName != null)
        {
            for (int i = 0; i < locations.Count; i++)
            {
                if (locations[i].GroupIndex == groupIndex
                && locations[i].Type == ownerType
                && locations[i].Name == ownerName)
                {
                    return i;
                }
            }
        }

        if (entry.Binding > 0)
        {
            for (int i = 0; i < locations.Count; i++)
            {
                if (locations[i].GroupIndex == groupIndex
                && locations[i].Type == ownerType
                && locations[i].Binding == entry.Binding - 1)
                {
                    return i;
                }
            }
        }

        throw new InvalidOperationException($"The companion entry '{entry.Name}' in bind group {groupIndex} has no owning {ownerType} resource. Pair it by name ('<resource name>{ShaderReflectionInfo.SamplerSuffix}' or '{ShaderReflectionInfo.CounterPrefix}<resource name>') or place it at the binding next to its owner.");
    }
}
