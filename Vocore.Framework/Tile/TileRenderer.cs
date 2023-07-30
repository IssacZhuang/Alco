using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vocore
{
    public class TileRenderer : ICommandRenderer
    {
        private TileMap _tileMap;

        public bool CanDraw => throw new NotImplementedException();

        public TileRenderer(TileMap tileMap)
        {
            _tileMap = tileMap;
        }

        ~TileRenderer()
        {
        }

        public void Draw(CommandBuffer commandBuffer)
        {
            throw new NotImplementedException();
        }



    }
}