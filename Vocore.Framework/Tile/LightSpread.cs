using System;
using Unity.Mathematics;

namespace Vocore
{
    public struct LightSpread
    {
        public static readonly LightSpread Default = new LightSpread
        {
            fixedColor = LightColor.Default,
            dynamicColor = LightColor.Default,
        };

        public LightColor fixedColor;
        public LightColor dynamicColor;

        public void SetFixedColor(int r, int g, int b)
        {
            fixedColor = new LightColor(r, g, b);
        }

        public void SetDynamicColor(int r, int g, int b)
        {
            dynamicColor = new LightColor(r, g, b);
        }
        public LightColor GetColor()
        {
            return fixedColor + dynamicColor;
        }

    }
}