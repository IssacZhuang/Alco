using Vocore.Graphics;

namespace Vocore.Rendering;
//todo: opt exception message
public class GraphicsMaterial : Material
{
    private const int RenderTextureIndexDepth = -1;
    protected enum ResourceType
    {
        Unavailable,
        TextureWithSampler,
        Buffer
    }


    protected struct Slot
    {
        public ResourceType type;
        public GraphicsBuffer? buffer;
        public Texture2D? texture;
        public RenderTexture? renderTexture;
        public int renderTextureIndex;
    }

    private readonly RenderingSystem _system;
    private readonly ArrayBuffer<Slot> _slots;
    private readonly Shader _shader;

    private bool _isPipelineDirty = true;

    private GraphicsPipelineContext _pipelineInfo;

    private DepthStencilState _depthStencilState = DepthStencilState.Default;
    private BlendState _blendState = BlendState.Opaque;
    private RasterizerState _rasterizerState = RasterizerState.CullNone;
    private PrimitiveTopology _primitiveTopology = PrimitiveTopology.TriangleList;

    public DepthStencilState DepthStencilState
    {
        get => _depthStencilState;
        set
        {
            _depthStencilState = value;
            _isPipelineDirty = true;
        }
    }

    public BlendState BlendState
    {
        get => _blendState;
        set
        {
            _blendState = value;
            _isPipelineDirty = true;
        }
    }

    public RasterizerState RasterizerState
    {
        get => _rasterizerState;
        set
        {
            _rasterizerState = value;
            _isPipelineDirty = true;
        }
    }

    public PrimitiveTopology PrimitiveTopology
    {
        get => _primitiveTopology;
        set
        {
            _primitiveTopology = value;
            _isPipelineDirty = true;
        }
    }

    public uint? StencilReference;

    internal GraphicsMaterial(RenderingSystem system, Shader shader, string name)
    {
        _system = system;
        _shader = shader;
        _slots = new ArrayBuffer<Slot>();

        _pipelineInfo = shader.GetGraphicsPipeline(
            system.PrefferedSDRPass,
            _depthStencilState,
            _blendState,
            _rasterizerState,
            _primitiveTopology
            );


        UpdateSlotResources();
    }

    #region  Set value

    public bool TrySetValue<T>(string name, T value) where T : unmanaged
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
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
        if (slot.type != ResourceType.Buffer)
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
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
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
        if (slot.type != ResourceType.Buffer)
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
        if (slot.type != ResourceType.TextureWithSampler)
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
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        ref Slot slot = ref _slots.GetRef(id);
        if (slot.type != ResourceType.TextureWithSampler)
        {
            throw new InvalidOperationException($"The resource {id} is not a texture.");
        }

        slot.renderTexture = null;
        slot.texture = texture;
    }


    #endregion

    #region Set render texture

    public bool TrySetRenderTexture(string name, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
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
        slot.renderTextureIndex = renderTextureIndex;
        return true;
    }

    public void SetRenderTexture(string name, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        if (!_pipelineInfo.TryGetResourceId(name, out uint id))
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
        slot.renderTextureIndex = renderTextureIndex;
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
        slot.renderTextureIndex = RenderTextureIndexDepth;
    }

    #endregion


    public override GPUPipeline GetPipeline(GPURenderPass renderPass)
    {
        if (_isPipelineDirty)
        {
            _pipelineInfo = _shader.GetGraphicsPipeline(
                renderPass,
                _depthStencilState,
                _blendState,
                _rasterizerState,
                _primitiveTopology
                );
            _isPipelineDirty = false;
        }

        if (_shader.TryUpdatePipelineInfo(ref _pipelineInfo, renderPass))
        {
            UpdateSlotResources();
        }

        return _pipelineInfo.Pipeline;
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
            if (slot.type == ResourceType.Buffer)
            {
                context.SetGraphicsResources(i, slot.buffer!.EntryReadonly);
            }
            else if (slot.type == ResourceType.TextureWithSampler)
            {
                if (slot.texture != null)
                {
                    context.SetGraphicsResources(i, slot.texture!.EntrySample);
                }
                else if (slot.renderTexture != null)
                {
                    GPUResourceGroup entry = slot.renderTextureIndex == RenderTextureIndexDepth
                        ? slot.renderTexture.EntryDepthSample!
                        : slot.renderTexture.EntriesColorSample[slot.renderTextureIndex];
                    context.SetGraphicsResources(i, entry);
                }
            }
        }
    }

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
                if (slot.buffer != null)
                {
                    continue;
                }
                BindGroupEntryInfo info = bindGroupLayout.Bindings[0];
                slot.type = ResourceType.Buffer;
                slot.buffer = _system.CreateGraphicsBuffer(
                    info.Size,
                    $"Material_{_pipelineInfo.Pipeline.Name}_Buffer_{info.Entry.Name}"
                    );
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
            }
            else
            {
                slot.type = ResourceType.Unavailable;
            }
        }
    }
}