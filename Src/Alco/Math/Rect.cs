using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// A 2D rectangle
    /// </summary>
    public struct Rect
    {
        public Vector2 Origin;
        public Vector2 Size;

        public Rect(Vector2 positon, Vector2 size)
        {
            this.Origin = positon;
            this.Size = size;
        }

        public Rect(float x, float y, float width, float height)
        {
            this.Origin = new Vector2(x, y);
            this.Size = new Vector2(width, height);
        }


        public Vector2 Center
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Origin + Size * 0.5f;
        }


        /// <summary>
        /// the origin of the rectangle
        /// </summary>
        public Vector2 Min
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Origin;
        }

        /// <summary>
        /// origin + size
        /// </summary>
        public Vector2 Max
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Origin + Size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Vector2 point)
        {
            return point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Rect rect)
        {
            return rect.Min.X >= Min.X && rect.Max.X <= Max.X && rect.Min.Y >= Min.Y && rect.Max.Y <= Max.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(Rect rect)
        {
            return rect.Min.X <= Max.X && rect.Max.X >= Min.X && rect.Min.Y <= Max.Y && rect.Max.Y >= Min.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Merge(Rect rect)
        {
            var min = Vector2.Min(Min, rect.Min);
            var max = Vector2.Max(Max, rect.Max);
            return new Rect(min, max - min);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Extend(float amount)
        {
            return new Rect(Min - new Vector2(amount), Size + new Vector2(amount * 2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Extend(float horizontal, float vertical)
        {
            return new Rect(Min - new Vector2(horizontal, vertical), Size + new Vector2(horizontal * 2, vertical * 2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Extend(float left, float top, float right, float bottom)
        {
            return new Rect(Min - new Vector2(left, top), Size + new Vector2(left + right, top + bottom));
        }
    }
}