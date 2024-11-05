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

    private readonly Shader _shader;
    private readonly Slot[] _slots;
    private GPURenderPass? _renderPass;
    private GPUPipeline? _pipeline;

    internal GraphicsMaterial(RenderingSystem system, Shader shader, string name)
    {
        _shader = shader;
        _slots = new Slot[shader.BindGroupCount];

        ShaderReflectionInfo reflectionInfo = shader.Reflections;

        for (int i = 0; i < reflectionInfo.BindGroups.Count; i++)
        {
            BindGroupLayout bindGroupLayout = reflectionInfo.BindGroups[i];
            if (UtilsMaterial.IsUniformBufferGroup(bindGroupLayout.Bindings))
            {
                BindGroupEntryInfo info = bindGroupLayout.Bindings[0];
                _slots[i].type = MaterialResourceType.Buffer;
                _slots[i].buffer = system.CreateGraphicsBuffer(
                    info.Size,
                    $"Material_{name}_Buffer_{info.Entry.Name}"
                    );
            }
            else if (UtilsMaterial.IsTextureSamplerGroup(bindGroupLayout.Bindings))
            {
                _slots[i].type = MaterialResourceType.TextureWithSampler;
                _slots[i].texture = system.TextureWhite;
            }
            else
            {
                _slots[i].type = MaterialResourceType.Unavailable;
            }

        }
    }

    #region  Set value

    public bool TrySet<T>(string name, T value) where T : unmanaged
    {
        if (!_shader.TryGetResourceId(name, out uint id))
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

        Slot slot = _slots[id];
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
        if (!_shader.TryGetResourceId(name, out uint id))
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

        Slot slot = _slots[id];
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
        if (!_shader.TryGetResourceId(name, out uint id))
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

        Slot slot = _slots[id];
        if (slot.type != MaterialResourceType.TextureWithSampler)
        {
            return false;
        }

        slot.texture = texture;
        return true;
    }

    public void Set(string name, Texture2D texture)
    {
        if (!_shader.TryGetResourceId(name, out uint id))
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

        Slot slot = _slots[id];
        if (slot.type != MaterialResourceType.TextureWithSampler)
        {
            throw new InvalidOperationException($"The resource {id} is not a texture.");
        }

        slot.texture = texture;
    }


    #endregion

    public override GPUPipeline GetPipeline(GPURenderPass renderPass)
    {
        if (_renderPass != renderPass)
        {
            _renderPass = renderPass;
            _pipeline = _shader.GetPipelineVariant(renderPass);
        }

        return _pipeline!;
    }

    protected override void SetPipelineResources(MaterialCommandContext context)
    {
        for (uint i = 0; i < _slots.Length; i++)
        {
            Slot slot = _slots[i];
            if (slot.type == MaterialResourceType.Buffer)
            {
                context.SetGraphicsResources(i, _slots[i].buffer!.EntryReadonly);
            }
            else if (slot.type == MaterialResourceType.TextureWithSampler)
            {
                context.SetGraphicsResources(i, _slots[i].texture!.EntrySample);
            }
        }
    }
}