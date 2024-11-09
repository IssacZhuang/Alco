using Vocore.Graphics;

namespace Vocore.Rendering;

public sealed class GraphicsMaterial : Material
{
    private struct Slot
    {
        public MaterialResourceType type;
        public GraphicsBuffer? buffer;
        public Texture2D? texture;
    }

    private readonly RenderingSystem _system;
    private readonly ArrayBuffer<Slot> _slots;
    private readonly Shader _shader;
    private ShaderPipelineInfo _pipelineInfo;
    


    internal GraphicsMaterial(RenderingSystem system, Shader shader, string name)
    {
        _system = system;
        _shader = shader;
        _slots = new ArrayBuffer<Slot>();
        _pipelineInfo = shader.GetGraphicsPipeline(
            system.PrefferedSDRPass,
            DepthStencilState.Default,
            BlendState.Opaque
            );
        _slots.EnsureSize(_pipelineInfo.BindGroupCount);

        UpdateSlotResources();
    }

    #region  Set value

    public bool TrySet<T>(string name, T value) where T : unmanaged
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySet(id, value);
    }

    public unsafe bool TrySet<T>(uint id, T value) where T : unmanaged
    {
        if (id < 0 || id >= _slots.Length)
        {
            return false;
        }

        Slot slot = _slots.Get(id);
        if (slot.type != MaterialResourceType.Buffer)
        {
            return false;
        }

        if (sizeof(T) > slot.buffer!.Size)
        {
            return false;
        }

        slot.buffer!.UpdateBuffer(value);
        return true;
    }

    public void Set<T>(string name, T value) where T : unmanaged
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        Set(id, value);
    }

    public unsafe void Set<T>(uint id, T value) where T : unmanaged
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        Slot slot = _slots.Get(id);
        if (slot.type != MaterialResourceType.Buffer)
        {
            throw new InvalidOperationException($"The resource {id} is not a buffer.");
        }

        if (sizeof(T) > slot.buffer!.Size)
        {
            throw new ArgumentOutOfRangeException($"The size of {typeof(T).Name} is larger than the buffer size: {slot.buffer.Size}.");
        }

        slot.buffer!.UpdateBuffer(value);
    }

    #endregion

    #region Set texture

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

        Slot slot = _slots.Get(id);
        if (slot.type != MaterialResourceType.TextureWithSampler)
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
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        Slot slot = _slots.Get(id);
        if (slot.type != MaterialResourceType.TextureWithSampler)
        {
            throw new InvalidOperationException($"The resource {id} is not a texture.");
        }

        slot.texture = texture;
    }


    #endregion

    public override GPUPipeline GetPipeline(GPURenderPass renderPass)
    {
        if (_shader.TryUpdatePipelineInfo(ref _pipelineInfo, renderPass))
        {
            UpdateSlotResources();
        }

        return _pipelineInfo.Pipeline;
    }

    protected override void SetPipelineResources(MaterialCommandContext context)
    {
        for (uint i = 0; i < _slots.Length; i++)
        {
            int index = (int)i;
            Slot slot = _slots[index];
            if (slot.type == MaterialResourceType.Buffer)
            {
                context.SetGraphicsResources(i, _slots[index].buffer!.EntryReadonly);
            }
            else if (slot.type == MaterialResourceType.TextureWithSampler)
            {
                context.SetGraphicsResources(i, _slots[index].texture!.EntrySample);
            }
        }
    }

    private void UpdateSlotResources()
    {
        ShaderReflectionInfo reflectionInfo = _pipelineInfo.ReflectionInfo;

        //todo opt: avoid reallocation
        for (int i = 0; i < reflectionInfo.BindGroups.Count; i++)
        {
            ref Slot slot = ref _slots[i];

            BindGroupLayout bindGroupLayout = reflectionInfo.BindGroups[i];
            if (UtilsMaterial.IsUniformBufferGroup(bindGroupLayout.Bindings))
            {
                if (slot.buffer != null)
                {
                    continue;
                }
                BindGroupEntryInfo info = bindGroupLayout.Bindings[0];
                slot.type = MaterialResourceType.Buffer;
                slot.buffer = _system.CreateGraphicsBuffer(
                    info.Size,
                    $"Material_{_pipelineInfo.ModulesInfo.Name}_Buffer_{info.Entry.Name}"
                    );
            }
            else if (UtilsMaterial.IsTextureSamplerGroup(bindGroupLayout.Bindings))
            {
                if (slot.texture != null)
                {
                    continue;
                }
                slot.type = MaterialResourceType.TextureWithSampler;
                slot.texture = _system.TextureWhite;
            }
            else
            {
                slot.type = MaterialResourceType.Unavailable;
            }
        }
    }
}