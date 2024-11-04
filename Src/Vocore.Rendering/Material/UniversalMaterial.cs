using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class UniversalMaterial : Material
{
    private struct Slot
    {
        public bool hasValue;
        public GPUResourceGroup? group;
        public object? owner;//prevent GC to collect the owner of GPUResourceGroup
    }

    private readonly Shader _shader;
    private readonly Slot[] _slots;

    public Shader Shader
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _shader;
    }
 

    public UniversalMaterial(Shader shader)
    {
        _shader = shader;
        _slots = new Slot[shader.BindGroupCount];
    }

    public ShaderResource this[string name]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            switch (value)
            {
                case GraphicsBuffer buffer:
                    SetBuffer(name, buffer);
                    break;
                case Texture2D texture:
                    SetTexture2D(name, texture);
                    break;
                default:
                    throw new ArgumentException("The resource type is not supported.");
            }
        }
    }

    public override GPUPipeline GetPipeline(GPURenderPass renderPass)
    {
        return _shader.GetPipelineVariant(renderPass);
    }

    public void ClearAt(uint id)
    {
        _slots[id].hasValue = false;
        _slots[id].group = null;
        _slots[id].owner = null;
    }

    public void ClearAt(string name)
    {
        if (!_shader.TryGetResourceId(name, out var index))
        {
            return;
        }

        ClearAt(index);
    }

    public void ClearAll()
    {
        for (uint i = 0; i < _slots.Length; i++)
        {
            ClearAt(i);
        }
    }

    #region Set Buffer

    public bool TrySetBuffer(uint id, GraphicsBuffer buffer)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return false;
        }

        ShaderReflectionInfo info = _shader.Reflections;
        BindGroupLayout layout = info.BindGroups[(int)id];
        if (layout.Bindings.Count == 0)
        {
            return false;
        }

        BindGroupEntry entry = layout.Bindings[0];
        if (entry.Type == BindingType.UniformBuffer)
        {
            SetResource(id, buffer.EntryReadonly, buffer);
            return true;
        }
        else if (entry.Type == BindingType.StorageBuffer)
        {
            SetResource(id, buffer.EntryReadWrite, buffer);
            return true;
        }

        return false;
    }

    public bool TrySetBuffer(string name, GraphicsBuffer buffer)
    {
        if (!_shader.TryGetResourceId(name, out var index))
        {
            return false;
        }

        return TrySetBuffer(index, buffer);
    }

    public void SetBuffer(uint id, GraphicsBuffer buffer)
    {

        if (id < 0 || id >= _slots.Length)
        {
            throw new KeyNotFoundException($"The resource at index {id} is not found in the shader.");
        }

        ShaderReflectionInfo info = _shader.Reflections;
        BindGroupLayout layout = info.BindGroups[(int)id];
        if (layout.Bindings.Count == 0)
        {
            throw new KeyNotFoundException($"The resource at index {id} is not found in the shader.");
        }

        BindGroupEntry entry = layout.Bindings[0];
        if (entry.Type == BindingType.UniformBuffer)
        {
            SetResource(id, buffer.EntryReadonly, buffer);
            return;
        }
        else if (entry.Type == BindingType.StorageBuffer)
        {
            SetResource(id, buffer.EntryReadWrite, buffer);
            return;
        }

        throw new KeyNotFoundException($"The resource at index {id} is not a buffer in the shader.");
    }

    public void SetBuffer(string name, GraphicsBuffer buffer)
    {
        if (!_shader.TryGetResourceId(name, out var id))
        {
            throw new KeyNotFoundException($"The resource '{name}' is not found in the shader.");
        }

        ShaderReflectionInfo info = _shader.Reflections;
        BindGroupLayout layout = info.BindGroups[(int)id];
        if (layout.Bindings.Count == 0)
        {
            throw new KeyNotFoundException($"The resource '{name}' is not found in the shader.");
        }

        BindGroupEntry entry = layout.Bindings[0];
        if (entry.Type == BindingType.UniformBuffer)
        {
            SetResource(id, buffer.EntryReadonly, buffer);
            return;
        }
        else if (entry.Type == BindingType.StorageBuffer)
        {
            SetResource(id, buffer.EntryReadWrite, buffer);
            return;
        }

        throw new KeyNotFoundException($"The resource '{name}' is not a buffer in the shader.");
    }

    #endregion

    #region Set Texture 2D

    public bool TrySetTexture2D(uint id, Texture2D texture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            return false;
        }

        ShaderReflectionInfo info = _shader.Reflections;
        BindGroupLayout layout = info.BindGroups[(int)id];
        if (layout.Bindings.Count == 0)
        {
            return false;
        }

        BindGroupEntry entryTexture = layout.Bindings[0];
        if (entryTexture.Type == BindingType.Texture)
        {
            if (layout.Bindings.Count >= 2)
            {
                BindGroupEntry entrySampler = layout.Bindings[1];
                if (entrySampler.Type == BindingType.Sampler)
                {
                    SetResource(id, texture.EntrySample, texture);
                    return true;
                }
            }
            else
            {
                SetResource(id, texture.EntryReadonly, texture);
                return true;
            }
        }
        else if (entryTexture.Type == BindingType.StorageTexture)
        {
            SetResource(id, texture.EntryWriteable, texture);
            return true;
        }

        return false;
    }

    public bool TrySetTexture2D(string name, Texture2D texture)
    {
        if (!_shader.TryGetResourceId(name, out var index))
        {
            return false;
        }

        return TrySetTexture2D(index, texture);
    }

    public void SetTexture2D(uint id, Texture2D texture)
    {
        if (id < 0 || id >= _slots.Length)
        {
            throw new KeyNotFoundException($"The resource at index {id} is not found in the shader.");
        }

        ShaderReflectionInfo info = _shader.Reflections;
        BindGroupLayout layout = info.BindGroups[(int)id];
        if (layout.Bindings.Count == 0)
        {
            throw new KeyNotFoundException($"The resource at index {id} is not found in the shader.");
        }

        BindGroupEntry entryTexture = layout.Bindings[0];
        if (entryTexture.Type == BindingType.Texture)
        {
            if (layout.Bindings.Count >= 2)
            {
                BindGroupEntry entrySampler = layout.Bindings[1];
                if (entrySampler.Type == BindingType.Sampler)
                {
                    SetResource(id, texture.EntrySample, texture);
                    return;
                }
                throw new KeyNotFoundException($"The resource at index {id} has additional binding but it is not a sampler.");
            }
            else
            {
                SetResource(id, texture.EntryReadonly, texture);
                return;
            }
        }
        else if (entryTexture.Type == BindingType.StorageTexture)
        {
            SetResource(id, texture.EntryWriteable, texture);
            return;
        }

        throw new KeyNotFoundException($"The resource at index {id} is not a texture in the shader.");
    }

    public void SetTexture2D(string name, Texture2D texture)
    {
        if (!_shader.TryGetResourceId(name, out var id))
        {
            throw new KeyNotFoundException($"The resource '{name}' is not found in the shader.");
        }

        ShaderReflectionInfo info = _shader.Reflections;
        BindGroupLayout layout = info.BindGroups[(int)id];
        if (layout.Bindings.Count == 0)
        {
            throw new KeyNotFoundException($"The resource '{name}' is not found in the shader.");
        }

        BindGroupEntry entryTexture = layout.Bindings[0];
        if (entryTexture.Type == BindingType.Texture)
        {
            if (layout.Bindings.Count >= 2)
            {
                BindGroupEntry entrySampler = layout.Bindings[1];
                if (entrySampler.Type == BindingType.Sampler)
                {
                    SetResource(id, texture.EntrySample, texture);
                    return;
                }
                throw new KeyNotFoundException($"The resource '{name}' has additional binding but it is not a sampler.");
            }
            else
            {
                SetResource(id, texture.EntryReadonly, texture);
                return;
            }
        }
        else if (entryTexture.Type == BindingType.StorageTexture)
        {
            SetResource(id, texture.EntryWriteable, texture);
            return;
        }

        throw new KeyNotFoundException($"The resource '{name}' is not a texture in the shader.");
    }

    #endregion

    protected override void SetPipelineResources(MaterialCommandContext context)
    {
        if (_shader.IsGraphicsShader)
        {
            PushGraphicsResourceToCommandBuffer(context);
        }
        else
        {
            PushComputeResourceToCommandBuffer(context);
        }
    }

    private void PushGraphicsResourceToCommandBuffer(MaterialCommandContext context)
    {
        for (uint i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].hasValue)
            {
                context.SetGraphicsResources(i, _slots[i].group!);
            }
        }
    }

    private void PushComputeResourceToCommandBuffer(MaterialCommandContext context)
    {
        for (uint i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].hasValue)
            {
                context.SetComputeResources(i, _slots[i].group!);
            }
        }
    }

    private void SetResource(uint id, GPUResourceGroup group, object? owner = null)
    {
        _slots[id].hasValue = true;
        _slots[id].group = group;
        _slots[id].owner = owner;
    }

    
}

