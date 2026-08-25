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
/// <br/>Groups that resolve from a single slot (one resource plus its sampler)
/// are cached on the resource itself, keyed by the group
/// layout: they are created once per resource and reused across frames and
/// materials, so slots whose value changes per instance (e.g. the voxel GI
/// voxelize dispatch cycling mesh buffers) do not allocate new bind groups.
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
        StorageBuffer
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
        // The RenderTexture.Version recorded when the slot value was written (or when a
        // version drift was last detected), used by FlushResourceGroups to rebuild groups
        // whose render texture recreated its GPU resources in place.
        public uint renderTextureVersion;
    }

    private enum EntryKind : byte
    {
        Resource,
        OwnerSampler
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
        // Whether any slot of this group is a texture slot; groups without one skip the
        // render texture version validation in FlushResourceGroups entirely.
        public bool hasTextureSlots;
        // The fallback chain version at the time this group was last assembled.
        public int fallbackVersion;
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
    // Bumped on every slot value change, so dependent sets (instances using this set
    // as fallback) can tell that a re-resolution is needed. Monotonically increasing;
    // a sum of such versions over the fallback chain strictly increases on any change.
    private int _version;

    /// <summary>
    /// Get the reflection information of the shader.
    /// </summary>
    public ShaderReflectionInfo ReflectionInfo => _reflectionInfo;

    /// <summary>
    /// The parameter set used to resolve values for slots that have no value of their
    /// own (material instance parenting). Changes of the fallback values are tracked
    /// through a per-set version number, so <see cref="FlushResourceGroups"/> only
    /// re-resolves the groups when the fallback chain actually changed.
    /// </summary>
    internal ShaderParameterSet? Fallback
    {
        get => _fallback;
        set
        {
            _fallback = value;
            // Rewiring the fallback invalidates the recorded fallback versions.
            for (int i = 0; i < _groups.Length; i++)
            {
                _groups[i].dirty = true;
            }
        }
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
    public void SetReflectionInfo(ShaderReflectionInfo reflectionInfo, bool resetResources = false)    {
        ShaderReflectionInfo oldReflection = _reflectionInfo;
        Slot[] oldSlots = _slots;

        _reflectionInfo = reflectionInfo;
        BuildSlotsAndGroups();
        // Notify dependent sets that the fallback values must be re-resolved (the
        // resource locations changed even when the values are carried over).
        _version++;

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
            newSlot.renderTextureVersion = oldSlot.renderTextureVersion;
            // A depth-bound slot must stay depth-bound across the carry-over
            // (the depth view and comparison sampler are resolved from this flag).
            newSlot.isDepth = oldSlot.isDepth;
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
        && slot.type != ResourceType.StorageBuffer)
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
        && slot.type != ResourceType.StorageBuffer)
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
        if (id >= (uint)_slots.Length)
        {
            return null;
        }

        ref Slot slot = ref _slots[id];
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

    /// <summary>
    /// Sets the storage resource group of a single mip level of a 2D texture to a
    /// storage texture slot by name.
    /// </summary>
    /// <param name="name">The shader resource name of the storage texture.</param>
    /// <param name="texture">The 2D texture to bind.</param>
    /// <param name="mipLevel">The mip level to write (0 = full resolution).</param>
    public void SetTexture2DStorage(string name, Texture2D texture, uint mipLevel)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetTexture2DStorage(id, texture, mipLevel);
    }

    /// <summary>
    /// Sets the storage resource group of a single mip level of a 2D texture to a
    /// storage texture slot by index.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="texture">The 2D texture to bind.</param>
    /// <param name="mipLevel">The mip level to write (0 = full resolution).</param>
    public void SetTexture2DStorage(uint id, Texture2D texture, uint mipLevel)
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
    /// Sets the read-only resource group of a single mip level of a 2D texture to a
    /// read-only texture slot by name. Inside the bound view the mip is rebased to
    /// mip 0, so shaders load it with mip index 0.
    /// </summary>
    /// <param name="name">The shader resource name of the read-only texture.</param>
    /// <param name="texture">The 2D texture to bind.</param>
    /// <param name="mipLevel">The mip level to read (0 = full resolution).</param>
    public void SetTexture2DRead(string name, Texture2D texture, uint mipLevel)
    {
        if (!_reflectionInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetTexture2DRead(id, texture, mipLevel);
    }

    /// <summary>
    /// Sets the read-only resource group of a single mip level of a 2D texture to a
    /// read-only texture slot by index. Inside the bound view the mip is rebased to
    /// mip 0, so shaders load it with mip index 0.
    /// </summary>
    /// <param name="id">The shader resource ID of the resource.</param>
    /// <param name="texture">The 2D texture to bind.</param>
    /// <param name="mipLevel">The mip level to read (0 = full resolution).</param>
    public void SetTexture2DRead(uint id, Texture2D texture, uint mipLevel)
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
    /// Slang depth-texture type); a texture-only
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
            throw new InvalidOperationException($"The resource {id}({_reflectionInfo.GetResourceName(id)}) is not a depth texture. Declare it with a Slang depth texture type.");
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
    /// <br/>A group is skipped entirely when neither its own slots (dirty flag) nor
    /// any value of the fallback chain (version sum) changed, which makes the steady
    /// state a few integer comparisons per group with no allocation.
    /// <br/>Groups with texture slots additionally validate the recorded
    /// <see cref="RenderTexture.Version"/> of their render textures: an in-place
    /// <see cref="RenderTexture.Resize"/> keeps the slot reference intact but replaces
    /// the underlying GPU textures, which is detected here and marks the group dirty
    /// (bumping this set's version, so dependent sets re-resolve through the fallback
    /// chain as with any other value change).
    /// </summary>
    public void FlushResourceGroups()
    {
        // Every set version is monotonically increasing, so the sum strictly
        // increases whenever any value of the fallback chain changes.
        int fallbackVersion = 0;
        for (ShaderParameterSet? set = _fallback; set != null; set = set._fallback)
        {
            fallbackVersion += set._version;
        }

        for (int i = 0; i < _groups.Length; i++)
        {
            GroupState group = _groups[i];

            // A render texture resized in place keeps its object identity, so the slot
            // values look unchanged; the recorded version is the only signal that the
            // assembled group still references the destroyed textures.
            if (!group.dirty && group.hasTextureSlots)
            {
                EntryPlan[] plans = group.plans;
                for (int p = 0; p < plans.Length; p++)
                {
                    ref Slot slot = ref _slots[plans[p].slotIndex];
                    RenderTexture? renderTexture = slot.renderTexture;
                    if (renderTexture != null && renderTexture.Version != slot.renderTextureVersion)
                    {
                        slot.renderTextureVersion = renderTexture.Version;
                        MarkDirty(i);
                        break;
                    }
                }
            }

            if (!group.dirty && group.fallbackVersion == fallbackVersion)
            {
                continue;
            }

            group.dirty = false;
            group.fallbackVersion = fallbackVersion;
            group.current = AssembleGroup(i, group);
            _resourceGroups[i] = group.current;
        }
    }

    /// <summary>
    /// Releases the bind group layouts and cached resource groups assembled by this
    /// set. Slot values (textures and buffers) are caller-owned references and are
    /// not disposed. Called from the owning material's dispose path.
    /// </summary>
    internal void Dispose()
    {
        for (int i = 0; i < _groups.Length; i++)
        {
            GroupState group = _groups[i];
            group.layout?.Dispose();
            group.layout = null;
            group.current = null;
            foreach (GPUResourceGroup resourceGroup in group.cache.Values)
            {
                resourceGroup.Dispose();
            }
            group.cache.Clear();
            group.cacheOrder.Clear();
            _resourceGroups[i] = null;
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

        // Pass 1: resolve and hash (FNV-1a over the ordered (binding, resource
        // identity) pairs) without allocating; identical contents hit the cache.
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < plans.Length; i++)
        {
            IGPUBindableResource? value = ResolveEntryValue(in plans[i]);
            if (value == null)
            {
                // The group is not fully bound yet.
                return null;
            }

            hash = (hash ^ plans[i].binding) * 1099511628211UL;
            hash = (hash ^ (ulong)RuntimeHelpers.GetHashCode(value)) * 1099511628211UL;
        }

        // Single-slot groups (one resource plus its sampler companion)
        // are fully determined by the slot value and the group layout, so their
        // bind groups are cached on the resource itself and shared across frames
        // and materials. Binding new instances into such a group stops being a
        // native allocation once each resource has its group.
        if (TryAssembleSingleSlotGroup(groupIndex, group, plans, out GPUResourceGroup? resourceOwned))
        {
            return resourceOwned;
        }

        if (group.cache.TryGetValue(hash, out GPUResourceGroup? cached))
        {
            return cached;
        }

        // Pass 2 (cache miss only): resolve again into the binding entries and
        // build the group. Resolution is idempotent, so resolving twice is safe.
        group.layout ??= _device.CreateBindGroup(_reflectionInfo.BindGroups[groupIndex].ToDescriptor($"material_bind_group_layout_{groupIndex}"));

        ResourceBindingEntry[] resources = new ResourceBindingEntry[plans.Length];
        for (int i = 0; i < plans.Length; i++)
        {
            resources[i] = new ResourceBindingEntry(plans[i].binding, ResolveEntryValue(in plans[i])!);
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

    /// <summary>
    /// Assembles a group whose plans all resolve from one slot: one Resource
    /// entry plus an optional sampler companion of that same slot. The
    /// bind group of such a group is a pure function of the slot value and the
    /// group layout, so it is cached on the buffer or texture itself (see
    /// <see cref="GraphicsBuffer.GetOrCreateResourceGroup"/> and
    /// <see cref="Texture.GetOrCreateResourceGroup"/>) instead of the per-group
    /// LRU: created once per resource and reused no matter how often the slot
    /// changes, for this set or any other set binding the same resource.
    /// <br/>Slots resolved through the fallback chain or backed by render
    /// textures are not covered and fall back to the general cache.
    /// </summary>
    private bool TryAssembleSingleSlotGroup(int groupIndex, GroupState group, EntryPlan[] plans, out GPUResourceGroup? resourceGroup)
    {
        resourceGroup = null;
        int resourceIndex = -1;
        for (int i = 0; i < plans.Length; i++)
        {
            if (plans[i].kind == EntryKind.Resource)
            {
                if (resourceIndex >= 0)
                {
                    // More than one resource in the group.
                    return false;
                }

                resourceIndex = i;
            }
        }

        if (resourceIndex < 0)
        {
            return false;
        }

        int resourceSlot = plans[resourceIndex].slotIndex;
        uint samplerBinding = 0;
        bool hasSampler = false;
        bool comparisonSampler = false;
        for (int i = 0; i < plans.Length; i++)
        {
            if (plans[i].kind == EntryKind.Resource)
            {
                continue;
            }

            if (plans[i].slotIndex != resourceSlot)
            {
                // A companion of another resource: not a single-slot group.
                return false;
            }

            if (plans[i].kind == EntryKind.OwnerSampler)
            {
                hasSampler = true;
                samplerBinding = plans[i].binding;
                comparisonSampler = plans[i].entryType == BindingType.SamplerComparison;
            }
        }

        ref Slot slot = ref _slots[resourceSlot];
        uint resourceBinding = plans[resourceIndex].binding;
        if (slot.buffer != null)
        {
            group.layout ??= _device.CreateBindGroup(_reflectionInfo.BindGroups[groupIndex].ToDescriptor($"material_bind_group_layout_{groupIndex}"));
            resourceGroup = slot.buffer.GetOrCreateResourceGroup(group.layout, resourceBinding);
            return true;
        }

        if (slot.texture != null)
        {
            GPUTextureView? view = ResolveView(in slot);
            if (view == null)
            {
                return false;
            }

            GPUSampler? sampler = !hasSampler
                ? null
                : comparisonSampler ? _device.SamplerDepthComparison : ResolveSampler(in slot);
            group.layout ??= _device.CreateBindGroup(_reflectionInfo.BindGroups[groupIndex].ToDescriptor($"material_bind_group_layout_{groupIndex}"));
            resourceGroup = slot.texture.GetOrCreateResourceGroup(group.layout, view, sampler, resourceBinding, samplerBinding);
            return true;
        }

        // Render-texture-backed and fallback-resolved slots use the general path.
        return false;
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

        if (slot.mipView && texture is Texture2D texture2D)
        {
            return texture2D.GetMipView(slot.mipLevel);
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
        slot.renderTextureVersion = renderTexture?.Version ?? 0;
        MarkDirty(slot.groupIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkDirty(int groupIndex)
    {
        _groups[groupIndex].dirty = true;
        // Notify dependent sets (instances resolving through this set as fallback).
        _version++;
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

        // Pass 2: per-group entry plans; resolve sampler companions to the
        // texture resource that owns them.
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
                        int owner = FindOwnerSlot(locations, groupIndex, entry, BindingType.Texture);
                        plans[entryIndex].kind = EntryKind.OwnerSampler;
                        plans[entryIndex].slotIndex = owner;
                        ref Slot ownerSlot = ref _slots[owner];
                        if (ownerSlot.type == ResourceType.TextureRead)
                        {
                            ownerSlot.type = ResourceType.TextureWithSampler;
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

            bool hasTextureSlots = false;
            for (int i = 0; i < plans.Length; i++)
            {
                if (IsTextureSlot(_slots[plans[i].slotIndex].type))
                {
                    hasTextureSlots = true;
                    break;
                }
            }

            _groups[groupIndex] = new GroupState { plans = plans, dirty = true, hasTextureSlots = hasTextureSlots };
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

    // A sampler companion belongs to the texture at the preceding reflected
    // binding in the same bind group. Slang reflects the sampler kind directly;
    // no resource-name suffix participates in the decision.
    private static int FindOwnerSlot(IReadOnlyList<ShaderResourceLocation> locations, int groupIndex, in BindGroupEntry entry, BindingType ownerType)
    {
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

        throw new InvalidOperationException($"The sampler entry '{entry.Name}' in bind group {groupIndex} has no owning {ownerType} resource at the preceding binding.");
    }
}
