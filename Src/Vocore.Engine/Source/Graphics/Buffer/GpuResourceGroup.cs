using System;
using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{
    public class GpuResourceGroup
    {
        private readonly ResourceLayoutDescription[] _layouts;
        private readonly IGpuResource?[] _resources;
        private readonly IGpuBuffer?[] _buffers;
        public GpuResourceGroup(Shader shader) : this(shader.Reflection)
        {
        }
        public GpuResourceGroup(SpirvReflection reflection) : this(reflection.ResourceLayouts)
        {
        }
        public GpuResourceGroup(ResourceLayoutDescription[] layouts)
        {
            _layouts = layouts;
            _resources = new IGpuResource[_layouts.Length];
            _buffers = new IGpuBuffer[_layouts.Length];
        }

        /// <summary>
        /// Set the GPU buffer by the binding id<br/>
        /// Recommanded!!
        /// </summary>
        public bool TrySet(int id, IGpuResource resource)
        {
            if(resource == null)
            {
                Log.Warning("Try to set a null resource");
                return false;
            }

            if (id < 0 || id >= _layouts.Length)
            {
                return false;
            }

            if (_layouts[id].Elements.Length != 1)
            {
                return false;
            }

            _resources[id] = resource;
            if (resource is IGpuBuffer buffer)
            {
                _buffers[id] = buffer;
            }
            return true;
        }

        /// <summary>
        /// Set the GPU buffer by the binding id<br/>
        /// Not recommanded to use in the game loop, it's slow
        /// </summary>
        public bool TrySet(string name, IGpuResource resource)
        {
            return TrySet(GetId(name), resource);
        }

        /// <summary>
        /// Remove the resource in the group<br/>
        /// </summary>
        public bool TryRemove(int id)
        {
            if (id < 0 || id >= _layouts.Length)
            {
                return false;
            }

            if (_resources[id] == null)
            {
                return false;
            }

            _resources[id] = null;
            _buffers[id] = null;

            return false;
        }

        /// <summary>
        /// Remove the resource in the group<br/>
        /// </summary>
        public bool TryRemove(string name)
        {
            return TryRemove(GetId(name));
        }

        public int GetId(string name)
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
                IGpuBuffer buffer = _buffers[i]!;
                if (buffer != null)
                {
                    buffer.UpdateToGPU(commandList);
                }

                IGpuResource resource = _resources[i]!;
                if (resource  != null)
                {
                    commandList.SetGraphicsResourceSet(i, resource.ResourceSet);
                }
            }
        }
    }
}