using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Vocore
{
    public class BaseInstancedRenderManager<T> where T: InstancedRenderQueue
    {
        protected readonly List<T> _renderQueues;
        protected readonly Dictionary<(Mesh, Material), int> _cacheIndex;

        public BaseInstancedRenderManager()
        {
            _renderQueues = new List<T>();
            _cacheIndex = new Dictionary<(Mesh, Material), int>();
        }

        ~BaseInstancedRenderManager()
        {
            for (int i = 0; i < _renderQueues.Count; i++)
            {
                _renderQueues[i].Dispose();
            }
        }

        public int GetRendererID(Mesh mesh, Material material)
        {
            if (mesh == null)
            {
                throw ExceptionRendering.MeshIsMissing;
            }

            if (material == null)
            {
                throw ExceptionRendering.MaterialIsMissing;
            }

            if (_cacheIndex.TryGetValue((mesh, material), out int rendererID))
            {
                return rendererID;
            }
            else
            {
                T renderQueue = (T)Activator.CreateInstance(typeof(T), new object[] { mesh, material });
                _renderQueues.Add(renderQueue);
                rendererID = _renderQueues.Count - 1;
                _cacheIndex.Add((mesh, material), rendererID);
                return rendererID;
            }
        }

        public void AddInstance(int rendererID, Vector3 position, Quaternion rotattion, Vector3 scale)
        {
            if (rendererID < -1 || rendererID >= _renderQueues.Count)
            {
                throw ExceptionRendering.InvalidRenderId(0, _renderQueues.Count, rendererID);
            }
            _renderQueues[rendererID].AddInstance(position, rotattion, scale);
        }

        public void Draw()
        {
            for (int i = 0; i < _renderQueues.Count; i++)
            {
                _renderQueues[i].Draw();
            }
        }

        public void Draw(int rendererID)
        {
            _renderQueues[rendererID].Draw();
        }
    }
}
