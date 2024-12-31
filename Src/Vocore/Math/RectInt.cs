using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public struct RectInt
    {
        public int2 origin;
        public int2 size;

        public RectInt(int2 positon, int2 size)
        {
            this.origin = positon;
            this.size = size;
        }

        public RectInt(int x, int y, int width, int height)
        {
            this.origin = new int2(x, y);
            this.size = new int2(width, height);
        }

        public int2 Min
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return origin;
            }
        }

        public int2 Max
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return origin + size;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int2 point)
        {
            return point.x >= Min.x && point.x <= Max.x && point.y >= Min.y && point.y <= Max.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(RectInt rect)
        {
            return rect.Min.x >= Min.x && rect.Max.x <= Max.x && rect.Min.y >= Min.y && rect.Max.y <= Max.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(RectInt rect)
        {
            return rect.Min.x <= Max.x && rect.Max.x >= Min.x && rect.Min.y <= Max.y && rect.Max.y >= Min.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RectInt Merge(RectInt rect)
        {
            var min = math.min(Min, rect.Min);
            var max = math.max(Max, rect.Max);
            return new RectInt(min, max - min);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RectInt Extend(int amount)
        {
            return new RectInt(Min - new int2(amount), size + new int2(amount * 2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RectInt Extend(int horizontal, int vertical)
        {
            return new RectInt(Min - new int2(horizontal, vertical), size + new int2(horizontal * 2, vertical * 2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RectInt Extend(int left, int top, int right, int bottom)
        {
            return new RectInt(Min - new int2(left, top), size + new int2(left + right, top + bottom));
        }

        public Rect Normalize(float width, float height)
        {
            return new Rect(origin.x / width, origin.y / height, size.x / width, size.y / height);
        }

        public static implicit operator RectInt(Rect rect)
        {
            return new RectInt(rect.origin, rect.size);
        }

        public static implicit operator Rect(RectInt rect)
        {
            return new Rect(rect.origin, rect.size);
        }

        public static implicit operator System.Drawing.Rectangle(RectInt rect)
        {
            return new System.Drawing.Rectangle(rect.Min.x, rect.Min.y, rect.size.x, rect.size.y);
        }
        
        public static implicit operator RectInt(System.Drawing.Rectangle rect)
        {
            return new RectInt(rect.Location.X, rect.Location.Y, rect.Width, rect.Height);
        }
    }
}