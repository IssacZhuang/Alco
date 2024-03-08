using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class Shader : ShaderResource
{
    private readonly GPUPipeline _pipeline;
    private readonly ShaderReflectionInfo _reflectionInfo;
    private readonly Dictionary<string, uint> _resourceIds = new Dictionary<string, uint>();

    public GPUPipeline Pipeline
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipeline;
    }

    internal Shader(GPUPipeline pipeline, ShaderReflectionInfo reflectionInfo)
    {
        _pipeline = pipeline;
        _reflectionInfo = reflectionInfo;

        BuildResourceIndex();
    }

    public bool TryGetResourceId(string name, out uint resourceId)
    {
        return _resourceIds.TryGetValue(name, out resourceId);
    }

    private void BuildResourceIndex()
    {
        _resourceIds.Clear();
        for (uint i = 0; i < _reflectionInfo.BindGroups.Length; i++)
        {
            BindGroupLayout bindGroup = _reflectionInfo.BindGroups[i];
            if (bindGroup.Bindings != null
            && bindGroup.Bindings.Length > 0)
            {
                _resourceIds[bindGroup.Bindings[0].Name] = i;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        _pipeline.Dispose();
    }


}