using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// An integer rectangle defined by a minimum corner and a size.
    /// </summary>
    public struct RectInt
    {
        public int2 Origin;
        public int2 Size;

        /// <summary>
        /// Initializes a rectangle from its minimum corner and size.
        /// </summary>
        /// <param name="positon">The minimum corner of the rectangle.</param>
        /// <param name="size">The width and height of the rectangle.</param>
        public RectInt(int2 positon, int2 size)
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
        public RectInt(int x, int y, int width, int height)
        {
            this.Origin = new int2(x, y);
            this.Size = new int2(width, height);
        }

        public int2 Center
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Origin + Size / new int2(2, 2);
        }

        public int2 Min
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Origin;
            }
        }

        public int2 Max
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Origin + Size;
            }
        }

        /// <summary>
        /// Determines whether the rectangle contains the specified point, including its bounds.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point lies inside or on the rectangle's bounds.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int2 point)
        {
            return point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;
        }

        /// <summary>
        /// Determines whether the rectangle fully contains the specified rectangle.
        /// </summary>
        /// <param name="rect">The rectangle to test.</param>
        /// <returns>True if the specified rectangle lies entirely within this rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(RectInt rect)
        {
            return rect.Min.X >= Min.X && rect.Max.X <= Max.X && rect.Min.Y >= Min.Y && rect.Max.Y <= Max.Y;
        }

        /// <summary>
        /// Determines whether the rectangle overlaps the specified rectangle.
        /// </summary>
        /// <param name="rect">The rectangle to test.</param>
        /// <returns>True if the rectangles overlap, including touching edges.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(RectInt rect)
        {
            return rect.Min.X <= Max.X && rect.Max.X >= Min.X && rect.Min.Y <= Max.Y && rect.Max.Y >= Min.Y;
        }

        /// <summary>
        /// Returns the smallest rectangle that contains both this rectangle and the specified rectangle.
        /// </summary>
        /// <param name="rect">The rectangle to merge with.</param>
        /// <returns>The bounding rectangle of both rectangles.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RectInt Merge(RectInt rect)
        {
            var min = math.min(Min, rect.Min);
            var max = math.max(Max, rect.Max);
            return new RectInt(min, max - min);
        }

        /// <summary>
        /// Returns a copy expanded by the given amount on all sides.
        /// </summary>
        /// <param name="amount">The amount to expand each side by.</param>
        /// <returns>The expanded rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RectInt Extend(int amount)
        {
            return new RectInt(Min - new int2(amount), Size + new int2(amount * 2));
        }

        /// <summary>
        /// Returns a copy expanded by the given horizontal and vertical amounts.
        /// </summary>
        /// <param name="horizontal">The amount to expand the left and right sides by.</param>
        /// <param name="vertical">The amount to expand the top and bottom sides by.</param>
        /// <returns>The expanded rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RectInt Extend(int horizontal, int vertical)
        {
            return new RectInt(Min - new int2(horizontal, vertical), Size + new int2(horizontal * 2, vertical * 2));
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
        public RectInt Extend(int left, int top, int right, int bottom)
        {
            return new RectInt(Min - new int2(left, top), Size + new int2(left + right, top + bottom));
        }

        /// <summary>
        /// Returns the rectangle expressed in normalized coordinates for the given dimensions.
        /// </summary>
        /// <param name="width">The width divisor.</param>
        /// <param name="height">The height divisor.</param>
        /// <returns>The normalized rectangle.</returns>
        public Rect Normalize(float width, float height)
        {
            return new Rect(Origin.X / width, Origin.Y / height, Size.X / width, Size.Y / height);
        }

        /// <summary>
        /// Converts a floating-point rectangle to an integer rectangle.
        /// </summary>
        /// <param name="rect">The rectangle to convert.</param>
        /// <returns>The converted rectangle.</returns>
        public static implicit operator RectInt(Rect rect)
        {
            return new RectInt(rect.Origin, rect.Size);
        }

        /// <summary>
        /// Converts an integer rectangle to a floating-point rectangle.
        /// </summary>
        /// <param name="rect">The rectangle to convert.</param>
        /// <returns>The converted rectangle.</returns>
        public static implicit operator Rect(RectInt rect)
        {
            return new Rect(rect.Origin, rect.Size);
        }

        /// <summary>
        /// Converts the rectangle to a <see cref="System.Drawing.Rectangle"/> using its minimum corner and size.
        /// </summary>
        /// <param name="rect">The rectangle to convert.</param>
        /// <returns>The converted rectangle.</returns>
        public static implicit operator System.Drawing.Rectangle(RectInt rect)
        {
            return new System.Drawing.Rectangle(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y);
        }
        
        /// <summary>
        /// Converts a <see cref="System.Drawing.Rectangle"/> to an integer rectangle.
        /// </summary>
        /// <param name="rect">The rectangle to convert.</param>
        /// <returns>The converted rectangle.</returns>
        public static implicit operator RectInt(System.Drawing.Rectangle rect)
        {
            return new RectInt(rect.Location.X, rect.Location.Y, rect.Width, rect.Height);
        }

        /// <summary>
        /// Compares two <see cref="RectInt"/> instances for equality.
        /// </summary>
        /// <param name="left">The first rectangle.</param>
        /// <param name="right">The second rectangle.</param>
        /// <returns>True if the rectangles are equal, false otherwise.</returns>
        public static bool operator ==(RectInt left, RectInt right)
        {
            return left.Origin == right.Origin && left.Size == right.Size;
        }

        /// <summary>
        /// Compares two <see cref="RectInt"/> instances for inequality.
        /// </summary>
        /// <param name="left">The first rectangle.</param>
        /// <param name="right">The second rectangle.</param>
        /// <returns>True if the rectangles are not equal, false otherwise.</returns>
        public static bool operator !=(RectInt left, RectInt right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current rectangle.
        /// </summary>
        /// <param name="obj">The object to compare with the current rectangle.</param>
        /// <returns>True if the specified object is equal to the current rectangle; otherwise, false.</returns>
        public override bool Equals(object? obj)
        {
            return obj is RectInt other && Equals(other);
        }

        /// <summary>
        /// Determines whether the specified <see cref="RectInt"/> is equal to the current rectangle.
        /// </summary>
        /// <param name="other">The rectangle to compare with the current rectangle.</param>
        /// <returns>True if the specified rectangle is equal to the current rectangle; otherwise, false.</returns>
        public bool Equals(RectInt other)
        {
            return Origin.Equals(other.Origin) && Size.Equals(other.Size);
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current rectangle.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Origin, Size);
        }
    }
}