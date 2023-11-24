using System;
using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{
    public class GPUBufferGroup
    {
        private struct BindingGroup
        {
            public BindableResource[] resources;
        }
        private SpirvReflection _reflection;
        private BindingGroup[] _bindingGroups;
        private ShaderBufferId[] _bufferIds;
        public GPUBufferGroup(Shader shader) : this(shader.Reflection)
        {
        }
        public GPUBufferGroup(SpirvReflection reflection)
        {
            _reflection = reflection;
            _bindingGroups = new BindingGroup[reflection.ResourceLayouts.Length];
            int count = 0;
            for (int i = 0; i < reflection.ResourceLayouts.Length; i++)
            {
                _bindingGroups[i].resources = new BindableResource[reflection.ResourceLayouts[i].Elements.Length];
                count += reflection.ResourceLayouts[i].Elements.Length;
            }
            _bufferIds = new ShaderBufferId[count];
        }
        public BindableResource? GetResource(ShaderBufferId id)
        {
            return _bindingGroups[id.Set].resources[id.Binding];
        }

        public T? GetResource<T>(ShaderBufferId id) where T : class, BindableResource
        {
            return _bindingGroups[id.Set].resources[id.Binding] as T;
        }

        public bool TryGetResource(ShaderBufferId id, out BindableResource? resource)
        {
            resource = _bindingGroups[id.Set].resources[id.Binding];
            return resource != null;
        }

        public bool TryGetResource<T>(ShaderBufferId id, out T? resource) where T : class, BindableResource
        {
            resource = _bindingGroups[id.Set].resources[id.Binding] as T;
            return resource != null;
        }


    }
}