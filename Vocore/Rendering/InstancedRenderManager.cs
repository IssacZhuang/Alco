using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Vocore
{
    public class InstancedRenderManager
    {
        private static InstancedRenderManager _instance;
        public static InstancedRenderManager Default
        {
            get
            {
                if (_instance == null) _instance = new InstancedRenderManager();
                return _instance;
            }
        }

        private readonly List<InstancedRenderQueue> _renderQueues;
        private readonly Dictionary<(Mesh, Material), int> _cacheIndex;

        public InstancedRenderManager()
        {
            _renderQueues = new List<InstancedRenderQueue>();
            _cacheIndex = new Dictionary<(Mesh, Material), int>();
        }

        ~InstancedRenderManager()
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
                _renderQueues.Add(new InstancedRenderQueue(mesh, material));
                rendererID = _renderQueues.Count - 1;
                _cacheIndex.Add((mesh, material), rendererID);
                return rendererID;
            }
        }

        public void AddInstance(int rendererID, Vector3 position, Quaternion rotattion, Vector3 scale)
        {
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
