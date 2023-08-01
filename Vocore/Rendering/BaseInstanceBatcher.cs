using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vocore
{
    public class BaseInstanceBatcher
    {
        public static readonly int MaxCountPerBatch = 1023;
        private CommandBuffer _renderTarget;
        private Matrix4x4[] _matrices = new Matrix4x4[MaxCountPerBatch];
        private Material _material;
        private Mesh _mesh;
        private int _layer = 0;
        private int _count = 0;
        private MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
        public int Count => _count;
        public BaseInstanceBatcher(CommandBuffer renderTarget, Material material, Mesh mesh, int layer = 0)
        {
            if (mesh == null) throw ExceptionRendering.MeshIsMissing;
            if (material == null) throw ExceptionRendering.MaterialIsMissing;
            if (!material.enableInstancing) throw ExceptionRendering.MaterialNotInstanced;
            _renderTarget = renderTarget;
            _material = material;
            _mesh = mesh;
            _layer = layer;
        }

        protected virtual void UpdateData(Matrix4x4[] matrices, MaterialPropertyBlock propertyBlock)
        {

        }


        public void PushToBuffer()
        {
            if (_count == 0) return;
            UpdateData(_matrices, _propertyBlock);
            _renderTarget.DrawMeshInstanced(_mesh, 0, _material, _layer, _matrices, _count, _propertyBlock);
            _count = 0;
        }


    }
}