using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vocore{
    public class InstanceBatcher : BaseInstanceBatcher
    {
        private NativeBuffer<TransformData> _transformBuffer;
        public InstanceBatcher(CommandBuffer renderTarget, Material material, Mesh mesh, int layer = 0) : base(renderTarget, material, mesh, layer)
        {
        }

        


    }
}