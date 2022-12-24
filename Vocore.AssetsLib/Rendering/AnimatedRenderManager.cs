using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Vocore.AssetsLib
{
    public class AnimatedRenderManager: Singleton<AnimatedRenderManager>
    {
        private readonly List<AnimatedRenderQueue> _renderQueues;
        private readonly Dictionary<(Mesh, Material), int> _cacheIndex;

        public AnimatedRenderManager()
        {
            _renderQueues = new List<AnimatedRenderQueue>();
            _cacheIndex = new Dictionary<(Mesh, Material), int>();
        }

        ~AnimatedRenderManager()
        {
            for(int i=0; i < _renderQueues.Count; i++)
            {
                _renderQueues[i].Dispose();
            }
        }

        public int GetRendererID(Mesh mesh, Material material)
        {
            if (_cacheIndex.TryGetValue((mesh, material), out int rendererID))
            {
                return rendererID;
            }
            else
            {
                _renderQueues.Add(new AnimatedRenderQueue(mesh, material));
                rendererID = _renderQueues.Count - 1;
                _cacheIndex.Add((mesh, material), rendererID);
                return rendererID;
            }
        }

        public void AddInstance(int rendererID, Vector3 position, Quaternion rotattion, Vector3 scale, Color color = default, float frame = 0)
        {
            _renderQueues[rendererID].AddInstance(position, rotattion, scale, color, frame);
        }

        public void Draw(int rendererID)
        {
            _renderQueues[rendererID].Draw();
        }
    }
}
