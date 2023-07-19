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
        public static ColorInt Attenuation = new ColorInt(AttenuationPerTile, AttenuationPerTile, AttenuationPerTile, 0);
        public int2 position;
        public ColorInt color;

        public static TileLight Create(int2 position, ColorInt color)
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
                color = new ColorInt(color)
            };
        }
    }
}