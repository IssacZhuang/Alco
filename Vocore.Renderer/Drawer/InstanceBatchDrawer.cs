using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Vocore;
using UnityEngine;

namespace Vocore.Renderer
{
    public delegate void SetPropertyBlockForBatch(int start, int length, ref MaterialPropertyBlock propertyBlock);
    public class InstanceBatchDrawer
    {
        public const int MAX_COUNT_IN_BATCH = 1000;

        private Mesh _mesh;
        private Material _material;

        private readonly Matrix4x4[] _matrixBuffer;
        private MaterialPropertyBlock _propertyBlock;

        public SetPropertyBlockForBatch onUpdateBlockValues;

        public Mesh Mesh
        {
            get { return _mesh; }
            set { _mesh = value; }
        }

        public Material Material
        {
            get { return _material; }
            set {
                if (!value.enableInstancing) throw Exceptions.Exception_MaterialNotInstanced;
                _material = value; 
            }
        }

        public MaterialPropertyBlock PropertyBlock => _propertyBlock;

        public InstanceBatchDrawer()
        {
            _matrixBuffer = new Matrix4x4[MAX_COUNT_IN_BATCH];
            _propertyBlock = new MaterialPropertyBlock();
        }

        public InstanceBatchDrawer(Mesh mesh, Material material)
        {
            _matrixBuffer = new Matrix4x4[MAX_COUNT_IN_BATCH];
            _propertyBlock = new MaterialPropertyBlock();

            Mesh = mesh;
            Material = material;
        }

        public InstanceBatchDrawer(Mesh mesh, Shader shader)
        {
            _matrixBuffer = new Matrix4x4[MAX_COUNT_IN_BATCH];
            _propertyBlock = new MaterialPropertyBlock();
            
            Mesh = mesh;
            Material = MaterialUtility.CreateMaterial(shader, true);
        }

        public void Draw(IList<ITransform> transforms)
        {
            if (transforms.IsNullOrEmpty()) return;
            int index = 0;
            for(int i = 0; i < transforms.Count()/ MAX_COUNT_IN_BATCH; i++)
            {
                for(int j = 0; j < MAX_COUNT_IN_BATCH; j++)
                {
                    _matrixBuffer[j] = transforms[index].ToMatrix4x4();
                    index++;
                }
                Graphics.DrawMeshInstanced(Mesh, 0, Material, _matrixBuffer);
            }

            int remain = transforms.Count() % MAX_COUNT_IN_BATCH;
            if (remain > 0)
            {
                for(int i = 0; i < remain; i++)
                {
                    _matrixBuffer[i] = transforms[index].ToMatrix4x4();
                    index++;
                }
                
                Graphics.DrawMeshInstanced(Mesh, 0, Material, _matrixBuffer, remain);
            }
        }

        public void DrawWithProperty(IList<ITransform> transforms)
        {
            if (transforms.IsNullOrEmpty()) return;
            int index = 0;
            int fullBatchLength = transforms.Count() / MAX_COUNT_IN_BATCH;
            for (int i = 0; i < fullBatchLength; i++)
            {
                for (int j = 0; j < MAX_COUNT_IN_BATCH; j++)
                {
                    _matrixBuffer[j] = transforms[index].ToMatrix4x4();
                    index++;
                }
                onUpdateBlockValues?.Invoke(i * MAX_COUNT_IN_BATCH, (i + 1) * MAX_COUNT_IN_BATCH, ref _propertyBlock);
                Graphics.DrawMeshInstanced(Mesh, 0, Material, _matrixBuffer, MAX_COUNT_IN_BATCH, _propertyBlock);
            }

            int remain = transforms.Count() % MAX_COUNT_IN_BATCH;
            if (remain > 0)
            {
                for (int i = 0; i < remain; i++)
                {
                    _matrixBuffer[i] = transforms[index].ToMatrix4x4();
                    index++;
                }
                onUpdateBlockValues?.Invoke(fullBatchLength * MAX_COUNT_IN_BATCH, fullBatchLength * MAX_COUNT_IN_BATCH + remain, ref _propertyBlock);
                Graphics.DrawMeshInstanced(Mesh, 0, Material, _matrixBuffer, remain, PropertyBlock);
            }
        }
        
    }
}
