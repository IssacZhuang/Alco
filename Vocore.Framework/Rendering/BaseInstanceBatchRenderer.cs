using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vocore
{
    public class BaseInstanceBatchRenderer<T> where T : BaseInstanceBatcher
    {

        protected List<T> _batchers = new List<T>();
        protected readonly Dictionary<(Mesh, Material), int> _cacheIndex = new Dictionary<(Mesh, Material), int>();
        protected CommandBuffer _commandBuffer;
        public CommandBuffer CommandBuffer => _commandBuffer;

        public BaseInstanceBatchRenderer()
        {
            _commandBuffer = new CommandBuffer();
        }

        ~BaseInstanceBatchRenderer()
        {
            _commandBuffer.Dispose();
        }

        public int GetBatcherID(Mesh mesh, Material material)
        {
            if (mesh == null)
            {
                throw ExceptionRendering.MeshIsMissing;
            }

            if (material == null)
            {
                throw ExceptionRendering.MaterialIsMissing;
            }

            if (_cacheIndex.TryGetValue((mesh, material), out int batcherID))
            {
                return batcherID;
            }
            else
            {
                T batcher = (T)Activator.CreateInstance(typeof(T), new object[] { _commandBuffer, material, mesh });
                _batchers.Add(batcher);
                batcherID = _batchers.Count - 1;
                _cacheIndex.Add((mesh, material), batcherID);
                return batcherID;
            }
        }

        public void ResetBuffer()
        {
            _commandBuffer.Release();
        }

        public void PushToBuffer()
        {
            for (int i = 0; i < _batchers.Count; i++)
            {
                _batchers[i].PushToBuffer();
            }
        }
    }
}