using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct RectInt
    {
        public int2 Origin;
        public int2 Size;

        public RectInt(int2 positon, int2 size)
        {
            this.Origin = positon;
            this.Size = size;
        }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int2 point)
        {
            return point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(RectInt rect)
        {
            return rect.Min.X >= Min.X && rect.Max.X <= Max.X && rect.Min.Y >= Min.Y && rect.Max.Y <= Max.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(RectInt rect)
        {
            return rect.Min.X <= Max.X && rect.Max.X >= Min.X && rect.Min.Y <= Max.Y && rect.Max.Y >= Min.Y;
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
            return new RectInt(Min - new int2(amount), Size + new int2(amount * 2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RectInt Extend(int horizontal, int vertical)
        {
            return new RectInt(Min - new int2(horizontal, vertical), Size + new int2(horizontal * 2, vertical * 2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RectInt Extend(int left, int top, int right, int bottom)
        {
            return new RectInt(Min - new int2(left, top), Size + new int2(left + right, top + bottom));
        }

        public Rect Normalize(float width, float height)
        {
            return new Rect(Origin.X / width, Origin.Y / height, Size.X / width, Size.Y / height);
        }

        public static implicit operator RectInt(Rect rect)
        {
            return new RectInt(rect.Origin, rect.Size);
        }

        public static implicit operator Rect(RectInt rect)
        {
            return new Rect(rect.Origin, rect.Size);
        }

        public static implicit operator System.Drawing.Rectangle(RectInt rect)
        {
            return new System.Drawing.Rectangle(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y);
        }
        
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