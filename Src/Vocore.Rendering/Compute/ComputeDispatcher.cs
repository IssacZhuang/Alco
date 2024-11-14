using Vocore.Graphics;

namespace Vocore.Rendering;

//todo: opt exception message
public class ComputeDispatcher
{
    private const int RenderTextureIndexDepth = -1;
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
        public int renderTextureIndex;
    }

    private readonly GPUDevice _device;
    private readonly Shader _shader;
    private readonly ArrayBuffer<Slot> _slots;
    private ComputePipelineContext _pipelineInfo;


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

        if (x == 0 || y == 0 || z == 0)
        {
            throw new ArgumentOutOfRangeException($"The dispatch size must be greater than zero: {x}, {y}, {z}");
        }

        command.SetComputePipeline(_pipelineInfo.Pipeline);

        for (uint i = 0; i < _slots.Length; i++)
        {
            ref Slot slot = ref _slots.GetRef(i);
            switch (slot.type)
            {
                case ResourceType.UniformBuffer:
                    if (slot.buffer == null)
                    {
                        throw new InvalidOperationException($"The buffer resource {i} is not set.");
                    }
                    command.SetComputeResources(i, slot.buffer!.EntryReadonly);
                    break;
                case ResourceType.StorageBuffer:
                    if (slot.buffer == null)
                    {
                        throw new InvalidOperationException($"The buffer resource {i} is not set.");
                    }
                    command.SetComputeResources(i, slot.buffer!.EntryReadWrite);
                    break;
                case ResourceType.TextureWithSampler:
                    if (slot.texture != null)
                    {
                        command.SetComputeResources(i, slot.texture.EntrySample);
                    }
                    else if (slot.renderTexture != null)
                    {
                        GPUResourceGroup entry = slot.renderTextureIndex == RenderTextureIndexDepth ?
                            slot.renderTexture.EntryDepthSample! : slot.renderTexture.EntriesColorSample[slot.renderTextureIndex];
                        command.SetComputeResources(i, entry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"The texture resource {i} is not set.");
                    }
                    break;
                case ResourceType.TextureStorage:
                    if (slot.texture != null)
                    {
                        command.SetComputeResources(i, slot.texture.EntryWriteable);
                    }
                    else if (slot.renderTexture != null)
                    {
                        command.SetComputeResources(i, slot.renderTexture.EntriesColorWrite[slot.renderTextureIndex]);
                    }
                    else
                    {
                        throw new InvalidOperationException($"The texture resource {i} is not set.");
                    }
                    break;
                default:
                    break;
            }
        }

        command.DispatchCompute(x, y, z);

    }

    #region Set Buffer

    public bool TrySetBuffer(string name, GraphicsBuffer buffer)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
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
        if (slot.type != ResourceType.UniformBuffer && slot.type != ResourceType.StorageBuffer)
        {
            return false;
        }

        slot.buffer = buffer;
        return true;
    }

    public void SetBuffer(string name, GraphicsBuffer buffer)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
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
        if (slot.type != ResourceType.UniformBuffer && slot.type != ResourceType.StorageBuffer)
        {
            throw new InvalidOperationException($"The bind group {id} is not for a buffer.");
        }

        slot.buffer = buffer;
    }

    #endregion


    #region Set Texture

    public bool TrySetTexture(string name, Texture2D texture)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
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
        if (slot.type != ResourceType.TextureWithSampler &&
            slot.type != ResourceType.TextureStorage)
        {
            return false;
        }

        slot.renderTexture = null;
        slot.texture = texture;
        return true;
    }

    public void SetTexture(string name, Texture2D texture)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
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
        if (slot.type != ResourceType.TextureWithSampler &&
            slot.type != ResourceType.TextureStorage)
        {
            throw new InvalidOperationException($"The bind group {id} is not for a texture.");
        }

        slot.renderTexture = null;
        slot.texture = texture;
    }

    #endregion

    #region Set RenderTexture

    public bool TrySetRenderTexture(string name, RenderTexture renderTexture, int index = 0)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
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
        if (slot.type != ResourceType.TextureWithSampler &&
            slot.type != ResourceType.TextureStorage)
        {
            return false;
        }

        slot.texture = null;
        slot.renderTexture = renderTexture;
        slot.renderTextureIndex = index;
        return true;
    }

    public void SetRenderTexture(string name, RenderTexture renderTexture, int index = 0)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
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

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.TextureWithSampler &&
            slot.type != ResourceType.TextureStorage)
        {
            throw new InvalidOperationException($"The bind group {id} is not for a texture.");
        }

        slot.texture = null;
        slot.renderTexture = renderTexture;
        slot.renderTextureIndex = index;
    }

    public bool TrySetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
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

        slot.texture = null;
        slot.renderTexture = renderTexture;
        slot.renderTextureIndex = RenderTextureIndexDepth;
        return true;
    }

    public void SetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
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

        if(slot.type == ResourceType.TextureStorage)
        {
            throw new InvalidOperationException($"The bind group {id} is writeable but the depth texture is not supported.");
        }

        if (slot.type != ResourceType.TextureWithSampler)
        {
            throw new InvalidOperationException($"The bind group {id} is not for a texture.");
        }

        slot.texture = null;
        slot.renderTexture = renderTexture;
        slot.renderTextureIndex = RenderTextureIndexDepth;
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