using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Vocore;
using UnityEngine;


namespace Vocore.Renderer
{
    public delegate void SetMatricesForBatch(int start, int length, StructuredBuffer<Matrix4x4> matrices);
    public delegate void SetPropertyBlockForBatch(int start, int length, MaterialPropertyBlock propertyBlock);

    public class InstanceBatchDrawer
    {
        public const int MAX_COUNT_IN_BATCH = 1000;

        private Mesh _mesh;
        private Material _material;

        private readonly StructuredBuffer<Matrix4x4> _matrixBuffer;
        private readonly MaterialPropertyBlock _propertyBlock;

        public SetPropertyBlockForBatch onUpdateBlockValues;
        public SetMatricesForBatch onUpdateMatriceValues;

        public Mesh Mesh
        {
            get { return _mesh; }
            set { _mesh = value; }
        }

        public Material Material
        {
            get { return _material; }
            set
            {
                if (!value.enableInstancing) throw Exceptions.Exception_MaterialNotInstanced;
                _material = value;
            }
        }

        public MaterialPropertyBlock PropertyBlock => _propertyBlock;

        public InstanceBatchDrawer()
        {
            _matrixBuffer = new StructuredBuffer<Matrix4x4>(MAX_COUNT_IN_BATCH);
            _propertyBlock = new MaterialPropertyBlock();
        }

        public InstanceBatchDrawer(Mesh mesh, Material material)
        {
            _matrixBuffer = new StructuredBuffer<Matrix4x4>(MAX_COUNT_IN_BATCH);
            _propertyBlock = new MaterialPropertyBlock();

            Mesh = mesh;
            Material = material;
        }

        public InstanceBatchDrawer(Mesh mesh, Shader shader)
        {
            _matrixBuffer = new StructuredBuffer<Matrix4x4>(MAX_COUNT_IN_BATCH);
            _propertyBlock = new MaterialPropertyBlock();

            Mesh = mesh;
            Material = MaterialUtility.CreateMaterial(shader, true);
        }

        public void Draw(int count)
        {
            int fullBatchLength = count / MAX_COUNT_IN_BATCH;
            for (int i = 0; i < fullBatchLength; i++)
            {
                
                onUpdateMatriceValues?.Invoke(i * MAX_COUNT_IN_BATCH, MAX_COUNT_IN_BATCH, _matrixBuffer);
                Graphics.DrawMeshInstanced(Mesh, 0, Material, _matrixBuffer.Raw);
            }

            int remain = count % MAX_COUNT_IN_BATCH;
            if (remain > 0)
            {
                onUpdateMatriceValues?.Invoke(fullBatchLength * MAX_COUNT_IN_BATCH, remain, _matrixBuffer);
                Graphics.DrawMeshInstanced(Mesh, 0, Material, _matrixBuffer.Raw, remain);
            }
        }

        public void DrawWithProperty(int count)
        {
            int fullBatchLength = count / MAX_COUNT_IN_BATCH;
            for (int i = 0; i < fullBatchLength; i++)
            {
                onUpdateMatriceValues?.Invoke(i * MAX_COUNT_IN_BATCH, MAX_COUNT_IN_BATCH, _matrixBuffer);
                onUpdateBlockValues?.Invoke(i * MAX_COUNT_IN_BATCH, MAX_COUNT_IN_BATCH, _propertyBlock);
                Graphics.DrawMeshInstanced(Mesh, 0, Material, _matrixBuffer.Raw, MAX_COUNT_IN_BATCH, _propertyBlock);
            }

            int remain = count % MAX_COUNT_IN_BATCH;
            if (remain > 0)
            {
                onUpdateMatriceValues?.Invoke(fullBatchLength * MAX_COUNT_IN_BATCH, remain, _matrixBuffer);
                onUpdateBlockValues?.Invoke(fullBatchLength * MAX_COUNT_IN_BATCH, remain, _propertyBlock);
                Graphics.DrawMeshInstanced(Mesh, 0, Material, _matrixBuffer.Raw, remain, PropertyBlock);
            }
        }
    }
}
