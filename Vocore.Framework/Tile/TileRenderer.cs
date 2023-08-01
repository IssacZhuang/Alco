using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vocore
{
    public class TileRenderer : ICommandRenderer
    {
        private TileMap _tileMap;

        public TileRenderer(TileMap tileMap)
        {
            _tileMap = tileMap;
        }

        ~TileRenderer()
        {
        }

        public void Draw(CommandBuffer commandBuffer)
        {
            
        }



    }
}