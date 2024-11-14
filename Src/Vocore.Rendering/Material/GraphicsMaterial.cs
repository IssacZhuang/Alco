using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;
//todo: opt exception message
public sealed class GraphicsMaterial : Material
{
    private enum ResourceType
    {
        Unavailable,
        TextureWithSampler,
        UniformBuffer
    }


    private struct Slot
    {
        public ResourceType type;
        public GraphicsBuffer? buffer;
        public Texture2D? texture;
        public RenderTexture? renderTexture;
        public GPUResourceGroup? resourceGroup;
    }

    private readonly RenderingSystem _system;
    private readonly ArrayBuffer<Slot> _slots;
    private readonly Shader _shader;

    private bool _isPipelineDirty = true;

    private GraphicsPipelineContext _pipelineContext;

    public DepthStencilState DepthStencilState
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipelineContext.DepthStencil;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _pipelineContext.DepthStencil = value;
            _isPipelineDirty = true;
        }
    }

    public BlendState BlendState
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipelineContext.BlendState;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _pipelineContext.BlendState = value;
            _isPipelineDirty = true;
        }
    }

    public RasterizerState RasterizerState
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipelineContext.Rasterizer;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _pipelineContext.Rasterizer = value;
            _isPipelineDirty = true;
        }
    }

    public PrimitiveTopology PrimitiveTopology
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipelineContext.PrimitiveTopology;
        set
        {
            _pipelineContext.PrimitiveTopology = value;
            _isPipelineDirty = true;
        }
    }

    public Shader Shader
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _shader;
    }

    public string Name { get; } 

    public uint? StencilReference;

    internal GraphicsMaterial(RenderingSystem system, Shader shader, string name)
    {
        _system = system;
        _shader = shader;
        _slots = new ArrayBuffer<Slot>();
        Name = name;

        _pipelineContext = GraphicsPipelineContext.Default;
        _pipelineContext.ReflectionInfo = shader.GetShaderModules().ReflectionInfo;
        UpdateSlotResources();
    }

    #region  Set value

    public bool TrySetValue<T>(string name, T value) where T : unmanaged
    {
        if (!_pipelineContext.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySetValue(id, value);
    }

    public unsafe bool TrySetValue<T>(uint id, T value) where T : unmanaged
    {
        if (id < 0 || id >= _slots.Length)
        {
            return false;
        }

        Slot slot = _slots.Get(id);
        if (slot.type != ResourceType.UniformBuffer)
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

    public void SetValue<T>(string name, T value) where T : unmanaged
    {
        if (!_pipelineContext.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetValue(id, value);
    }

    public unsafe void SetValue<T>(uint id, T value) where T : unmanaged
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        Slot slot = _slots.Get(id);
        if (slot.type != ResourceType.UniformBuffer)
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

    public bool TrySetTexture(string name, Texture2D texture)
    {
        if (!_pipelineContext.TryGetResourceId(name, out uint id))
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
        if (slot.type != ResourceType.TextureWithSampler)
        {
            return false;
        }

        slot.renderTexture = null;
        slot.texture = texture;
        slot.resourceGroup = texture.EntrySample;
        return true;
    }

    public void SetTexture(string name, Texture2D texture)
    {
        if (!_pipelineContext.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetTexture(id, texture);
    }

    public void SetTexture(uint id, Texture2D texture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.TextureWithSampler)
        {
            throw new InvalidOperationException($"The resource {id} is not a texture.");
        }

        slot.renderTexture = null;
        slot.texture = texture;
        slot.resourceGroup = texture.EntrySample;
    }


    #endregion

    #region Set render texture

    public bool TrySetRenderTexture(string name, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        if (!_pipelineContext.TryGetResourceId(name, out uint id))
        {
            return false;
        }

        return TrySetRenderTexture(id, renderTexture, renderTextureIndex);
    }

    public bool TrySetRenderTexture(uint id, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return false;
        }

        if (renderTextureIndex < 0 || renderTextureIndex >= renderTexture.EntriesColorSample.Length)
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
        slot.resourceGroup = renderTexture.EntriesColorSample[renderTextureIndex];
        return true;
    }

    public void SetRenderTexture(string name, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        if (!_pipelineContext.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetRenderTexture(id, renderTexture, renderTextureIndex);
    }

    public void SetRenderTexture(uint id, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        if (renderTextureIndex < 0 || renderTextureIndex >= renderTexture.EntriesColorSample.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(renderTextureIndex));
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.TextureWithSampler)
        {
            throw new InvalidOperationException($"The resource {id} is not a texture.");
        }

        slot.texture = null;
        slot.renderTexture = renderTexture;
        slot.resourceGroup = renderTexture.EntriesColorSample[renderTextureIndex];
    }

    public bool TrySetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        if (!_pipelineContext.TryGetResourceId(name, out uint id))
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

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.TextureWithSampler)
        {
            return false;
        }

        slot.texture = null;
        slot.renderTexture = renderTexture;
        slot.resourceGroup = renderTexture.EntryDepthSample;
        return true;
    }

    public void SetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        if (!_pipelineContext.TryGetResourceId(name, out uint id))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        SetRenderTextureDepth(id, renderTexture);
    }

    public void SetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        if (!renderTexture.HasDepth)
        {
            throw new InvalidOperationException($"The render texture does not have a depth attachment.");
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.TextureWithSampler)
        {
            throw new InvalidOperationException($"The resource {id} is not a texture.");
        }

        slot.texture = null;
        slot.renderTexture = renderTexture;
        slot.resourceGroup = renderTexture.EntryDepthSample;
    }

    #endregion


    public override GPUPipeline GetPipeline(GPURenderPass renderPass)
    {
        if (_shader.TryUpdatePipelineContext(ref _pipelineContext, renderPass, _isPipelineDirty))
        {
            UpdateSlotResources();
            _isPipelineDirty = false;
        }

        return _pipelineContext.Pipeline!;
    }

    protected override void SetPipelineResources(MaterialCommandContext context)
    {
        if (StencilReference.HasValue)
        {
            context.SetStencilReference(StencilReference.Value);
        }

        for (uint i = 0; i < _slots.Length; i++)
        {
            int index = (int)i;
            Slot slot = _slots[index];
            if (slot.resourceGroup == null)
            {
                continue;
            }

            context.SetGraphicsResources(i, slot.resourceGroup);
        }
    }

    private void UpdateSlotResources()
    {
        ShaderReflectionInfo reflectionInfo = _pipelineContext.ReflectionInfo!;
        _slots.EnsureSize(reflectionInfo.BindGroups.Count);

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
                slot.type = ResourceType.UniformBuffer;
                slot.buffer = _system.CreateGraphicsBuffer(
                    info.Size,
                    $"Material_{Name}_Buffer_{info.Entry.Name}"
                    );
                slot.resourceGroup = slot.buffer.EntryReadonly;
            }
            else if (UtilsMaterial.IsTextureSamplerGroup(bindGroupLayout.Bindings))
            {
                if (slot.texture != null||
                    slot.renderTexture != null)
                {
                    continue;
                }
                slot.type = ResourceType.TextureWithSampler;
                slot.texture = _system.TextureWhite;
                slot.resourceGroup = slot.texture.EntrySample;
            }
            else
            {
                slot.type = ResourceType.Unavailable;
                slot.buffer = null;
                slot.texture = null;
                slot.renderTexture = null;
                slot.resourceGroup = null;
            }
        }
    }
}