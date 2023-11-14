using System;


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

        public AABBInt Intersection(AABBInt other)
        {
            int x1 = math.max(min.x, other.min.x);
            int x2 = math.min(max.x, other.max.x);
            int y1 = math.max(min.y, other.min.y);
            int y2 = math.min(max.y, other.max.y);
            return new AABBInt(x1, y1, x2 - x1, y2 - y1);
        }

    }
}