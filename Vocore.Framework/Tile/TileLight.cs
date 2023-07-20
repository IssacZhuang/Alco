using System;

using UnityEngine;
using Unity.Mathematics;

namespace Vocore
{
    public struct TileLight
    {
        public static readonly int MaxIntensity = 255;
        public static readonly int MaxRadius = 16;
        public static int AttenuationPerTile = MaxIntensity / MaxRadius;
        public int2 position;
        public LightColor color;

        public static TileLight Create(int2 position, LightColor color)
        {
            TileLight result = new TileLight
            {
                position = position,
                color = color,
            };
            return result;
        }

        public static TileLight Create(Vector2 position, Color color)
        {
            return new TileLight
            {
                position = new int2((int)position.x, (int)position.y),
                color = new LightColor(color)
            };
        }
    }
}