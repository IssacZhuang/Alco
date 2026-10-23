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
        /// <summary>
        /// A unit rectangle at the origin.
        /// </summary>
        public static readonly Rect One = new Rect(0, 0, 1, 1);

        public Vector2 Origin;
        public Vector2 Size;

        /// <summary>
        /// Initializes a rectangle from its minimum corner and size.
        /// </summary>
        /// <param name="positon">The minimum corner of the rectangle.</param>
        /// <param name="size">The width and height of the rectangle.</param>
        public Rect(Vector2 positon, Vector2 size)
        {
            this.Origin = positon;
            this.Size = size;
        }

        /// <summary>
        /// Initializes a rectangle from its minimum corner and dimensions.
        /// </summary>
        /// <param name="x">The X coordinate of the minimum corner.</param>
        /// <param name="y">The Y coordinate of the minimum corner.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
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

        /// <summary>
        /// Determines whether the rectangle contains the specified point, including its bounds.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point lies inside or on the rectangle's bounds.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Vector2 point)
        {
            return point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;
        }

        /// <summary>
        /// Determines whether the rectangle fully contains the specified rectangle.
        /// </summary>
        /// <param name="rect">The rectangle to test.</param>
        /// <returns>True if the specified rectangle lies entirely within this rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Rect rect)
        {
            return rect.Min.X >= Min.X && rect.Max.X <= Max.X && rect.Min.Y >= Min.Y && rect.Max.Y <= Max.Y;
        }

        /// <summary>
        /// Determines whether the rectangle overlaps the specified rectangle.
        /// </summary>
        /// <param name="rect">The rectangle to test.</param>
        /// <returns>True if the rectangles overlap, including touching edges.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(Rect rect)
        {
            return rect.Min.X <= Max.X && rect.Max.X >= Min.X && rect.Min.Y <= Max.Y && rect.Max.Y >= Min.Y;
        }

        /// <summary>
        /// Returns the smallest rectangle that contains both this rectangle and the specified rectangle.
        /// </summary>
        /// <param name="rect">The rectangle to merge with.</param>
        /// <returns>The bounding rectangle of both rectangles.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Merge(Rect rect)
        {
            var min = Vector2.Min(Min, rect.Min);
            var max = Vector2.Max(Max, rect.Max);
            return new Rect(min, max - min);
        }

        /// <summary>
        /// Returns a copy expanded by the given amount on all sides.
        /// </summary>
        /// <param name="amount">The amount to expand each side by.</param>
        /// <returns>The expanded rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Extend(float amount)
        {
            return new Rect(Min - new Vector2(amount), Size + new Vector2(amount * 2));
        }

        /// <summary>
        /// Returns a copy expanded by the given horizontal and vertical amounts.
        /// </summary>
        /// <param name="horizontal">The amount to expand the left and right sides by.</param>
        /// <param name="vertical">The amount to expand the top and bottom sides by.</param>
        /// <returns>The expanded rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Extend(float horizontal, float vertical)
        {
            return new Rect(Min - new Vector2(horizontal, vertical), Size + new Vector2(horizontal * 2, vertical * 2));
        }

        /// <summary>
        /// Returns a copy expanded by the given amount on each side.
        /// </summary>
        /// <param name="left">The amount to expand the left side by.</param>
        /// <param name="top">The amount to expand the top side by.</param>
        /// <param name="right">The amount to expand the right side by.</param>
        /// <param name="bottom">The amount to expand the bottom side by.</param>
        /// <returns>The expanded rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Extend(float left, float top, float right, float bottom)
        {
            return new Rect(Min - new Vector2(left, top), Size + new Vector2(left + right, top + bottom));
        }

        /// <summary>
        /// Converts this Rect to a RectInt by rounding the floating-point values to the nearest integers.
        /// </summary>
        /// <returns>A <see cref="RectInt"/> with coordinates rounded to the nearest integers.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RectInt RoundToInt()
        {
            int2 roundedOrigin = math.round(Origin);
            int2 roundedSize = math.round(Size);
            return new RectInt(roundedOrigin, roundedSize);
        }
    }
}