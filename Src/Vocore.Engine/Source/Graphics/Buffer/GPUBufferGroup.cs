using System;
using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{
    public class GpuBufferGroup
    {
        private readonly ResourceLayoutDescription[] _layouts;
        private readonly IGpuResource?[] _resources;
        public GpuBufferGroup(Shader shader) : this(shader.Reflection)
        {
        }
        public GpuBufferGroup(SpirvReflection reflection) : this(reflection.ResourceLayouts)
        {
        }
        public GpuBufferGroup(ResourceLayoutDescription[] layouts)
        {
            _layouts = layouts;
            _resources = new IGpuResource[_layouts.Length];
        }

        /// <summary>
        /// Set the GPU buffer by the binding id<br/>
        /// Recommanded!!
        /// </summary>
        public bool TrySet(int bindingId, IGpuResource resource)
        {
            if (bindingId < 0 || bindingId >= _layouts.Length)
            {
                return false;
            }

            if (_layouts[bindingId].Elements.Length != 1)
            {
                return false;
            }

            _resources[bindingId] = resource;

            return true;
        }

        /// <summary>
        /// Set the GPU buffer by the binding id<br/>
        /// Not recommanded to use in the game loop, it's slow
        /// </summary>
        public bool TrySet(string name, IGpuResource resource)
        {
            return TrySet(GetBindingId(name), resource);
        }

        /// <summary>
        /// Remove the resource in the group<br/>
        /// </summary>
        public bool TryRemove(int bindingId)
        {
            if (bindingId < 0 || bindingId >= _layouts.Length)
            {
                return false;
            }

            if (_resources[bindingId] == null)
            {
                return false;
            }

            _resources[bindingId] = null;

            return false;
        }

        /// <summary>
        /// Remove the resource in the group<br/>
        /// </summary>
        public bool TryRemove(string name)
        {
            return TryRemove(GetBindingId(name));
        }

        public int GetBindingId(string name)
        {
            for (int i = 0; i < _layouts.Length; i++)
            {
                for (int j = 0; j < _layouts[i].Elements.Length; j++)
                {
                    if (_layouts[i].Elements[j].Name == name)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        public void SetResourceForCommandList(CommandList commandList)
        {
            for (uint i = 0; i < _layouts.Length; i++)
            {
                if (_resources[i] != null)
                {
                    commandList.SetGraphicsResourceSet(i, _resources[i].ResourceSet);
                }
            }
        }
    }
}