using System;
using Unity.Mathematics;

namespace Vocore
{
    public struct AABBInt
    {
        public int2 min;
        public int2 max;

        public int Width => max.x - min.x;
        public int Height => max.y - min.y;

        public AABBInt(int2 min, int2 max)
        {
            this.min = min;
            this.max = max;
        }

        public AABBInt(int x, int y, int width, int height)
        {
            min = new int2(x, y);
            max = new int2(x + width, y + height);
        }

        public bool Contains(int2 point)
        {
            return point.x >= min.x && point.x <= max.x && point.y >= min.y && point.y <= max.y;
        }

        public bool Intersects(AABBInt other)
        {
            return min.x <= other.max.x && max.x >= other.min.x && min.y <= other.max.y && max.y >= other.min.y;
        }

    }
}