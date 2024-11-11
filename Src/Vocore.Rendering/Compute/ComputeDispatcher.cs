using Vocore.Graphics;

namespace Vocore.Rendering;

public class ComputeDispatcher
{
    protected enum ResourceType
    {
        Unavailable,
        TextureWithSampler,
        TextureStorage,
        UniformBuffer,
        StorageBuffer
    }

    protected struct Slot
    {
        public ResourceType type;
        public GraphicsBuffer? buffer;
        public Texture2D? texture;
        public RenderTexture? renderTexture;
    }

    private readonly GPUDevice _device;
    private readonly Shader _shader;
    private readonly ArrayBuffer<Slot> _slots;
    private ComputePipelineInfo _pipelineInfo;


    internal ComputeDispatcher(RenderingSystem system, Shader shader)
    {
        _device = system.GraphicsDevice;
        _shader = shader;
        _pipelineInfo = shader.GetComputePipelineInfo();
        _slots = new ArrayBuffer<Slot>();

        UpdateSlotResources();
    }

    internal ComputeDispatcher(RenderingSystem system, Shader shader, ReadOnlySpan<string> defines)
    {
        _device = system.GraphicsDevice;
        _shader = shader;
        _pipelineInfo = shader.GetComputePipelineInfo(defines);
        _slots = new ArrayBuffer<Slot>();

        UpdateSlotResources();
    }

    public void Dispatch(GPUCommandBuffer command, uint x, uint y, uint z)
    {
        if (!command.IsRecording)
        {
            throw new InvalidOperationException("The command buffer is not in recording. Try uses GPUCommandBuffer.Begin()");
        }
    }

    #region Set Buffer

    public bool TrySet(string name, GraphicsBuffer buffer)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySet(id, buffer);
    }


    public bool TrySet(uint id, GraphicsBuffer buffer)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return false;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.UniformBuffer && slot.type != ResourceType.StorageBuffer)
        {
            return false;
        }

        slot.buffer = buffer;
        return true;
    }

    public void Set(string name, GraphicsBuffer buffer)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        Set(id, buffer);
    }

    public void Set(uint id, GraphicsBuffer buffer)
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.UniformBuffer && slot.type != ResourceType.StorageBuffer)
        {
            throw new InvalidOperationException($"The bind group {id} is not for a buffer.");
        }

        slot.buffer = buffer;
    }

    #endregion


    #region Set Texture

    public bool TrySet(string name, Texture2D texture)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySet(id, texture);
    }

    public bool TrySet(uint id, Texture2D texture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return false;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.TextureWithSampler &&
            slot.type != ResourceType.TextureStorage)
        {
            return false;
        }

        slot.texture = texture;
        return true;
    }

    public void Set(string name, Texture2D texture)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        Set(id, texture);
    }

    public void Set(uint id, Texture2D texture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.TextureWithSampler &&
            slot.type != ResourceType.TextureStorage)
        {
            throw new InvalidOperationException("The resource is not a texture.");
        }

        slot.texture = texture;
    }

    #endregion

    #region Set RenderTexture

    public bool TrySet(string name, RenderTexture renderTexture)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySet(id, renderTexture);
    }

    public bool TrySet(uint id, RenderTexture renderTexture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return false;
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.TextureWithSampler &&
            slot.type != ResourceType.TextureStorage)
        {
            return false;
        }

        slot.renderTexture = renderTexture;
        return true;
    }

    public void Set(string name, RenderTexture renderTexture)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        Set(id, renderTexture);
    }

    public void Set(uint id, RenderTexture renderTexture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "The resource ID is out of range.");
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.TextureWithSampler &&
            slot.type != ResourceType.TextureStorage)
        {
            throw new InvalidOperationException("The resource is not a texture.");
        }

        slot.renderTexture = renderTexture;
    }

    #endregion


    private void UpdateSlotResources()
    {
        ShaderReflectionInfo reflectionInfo = _pipelineInfo.ReflectionInfo;
        _slots.EnsureSize(_pipelineInfo.BindGroupCount);

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
                throw new NotSupportedException($"Unsupported bind group layout for compute shader:\n{bindGroupLayout}");
            }
        }
    }



}