using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Alco
{
    /// <summary>
    /// An internal node of a 4-wide bounding volume hierarchy.
    /// The layout is part of the build contract between <see cref="NativeBvh2D"/> and
    /// <see cref="IBvhBuilder2D"/>: the bounds of the (up to) four children are stored
    /// structure-of-arrays style, so one traversal step tests all children with a single
    /// <see cref="Vector128{T}"/> comparison per axis, and the child references are sign-tagged.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 80)]
    public unsafe struct BvhNode2D
    {
        /// <summary>
        /// The minimum X of child 0..3, one lane per child.
        /// </summary>
        public Vector128<float> LowerX;

        /// <summary>
        /// The maximum X of child 0..3, one lane per child.
        /// </summary>
        public Vector128<float> UpperX;

        /// <summary>
        /// The minimum Y of child 0..3, one lane per child.
        /// </summary>
        public Vector128<float> LowerY;

        /// <summary>
        /// The maximum Y of child 0..3, one lane per child.
        /// </summary>
        public Vector128<float> UpperY;

        /// <summary>
        /// The tagged child references. Lane i describes the child whose bounds occupy lane i of
        /// the rows above: a value >= 0 addresses the node buffer, a value &lt; 0 (other than
        /// <see cref="EmptyChild"/>) addresses leaf block ~value, and <see cref="EmptyChild"/>
        /// marks an unused lane whose bounds must fail every traversal test.
        /// </summary>
        public fixed int Children[4];

        /// <summary>
        /// The tagged reference marking an unused child lane.
        /// </summary>
        public const int EmptyChild = int.MinValue;

        /// <summary>
        /// Encodes a leaf block index into a tagged child reference.
        /// </summary>
        public static int EncodeLeaf(int leafIndex)
        {
            return ~leafIndex;
        }

        /// <summary>
        /// Decodes a tagged child reference into a leaf block index; the reference must be negative.
        /// </summary>
        public static int DecodeLeaf(int childRef)
        {
            return ~childRef;
        }
    }
}
