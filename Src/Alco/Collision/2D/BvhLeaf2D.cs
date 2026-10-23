using System;
using System.Numerics;
using System.Runtime.Intrinsics;

namespace Alco
{
    /// <summary>
    /// A leaf block of a 4-wide bounding volume hierarchy storing up to four colliders in structure-of-arrays form.
    /// Unused slots carry empty bounds and null <see cref="ColliderRef2D"/> values so they fail every traversal test.
    /// </summary>
    public unsafe struct BvhLeaf2D
    {
        /// <summary>
        /// The minimum X of slot 0..3, one lane per slot.
        /// </summary>
        public Vector128<float> LowerX;

        /// <summary>
        /// The maximum X of slot 0..3, one lane per slot.
        /// </summary>
        public Vector128<float> UpperX;

        /// <summary>
        /// The minimum Y of slot 0..3, one lane per slot.
        /// </summary>
        public Vector128<float> LowerY;

        /// <summary>
        /// The maximum Y of slot 0..3, one lane per slot.
        /// </summary>
        public Vector128<float> UpperY;

        /// <summary>
        /// The collider of slot 0, or a null reference when the slot is unused.
        /// </summary>
        public ColliderRef2D C0;

        /// <summary>
        /// The collider of slot 1, or a null reference when the slot is unused.
        /// </summary>
        public ColliderRef2D C1;

        /// <summary>
        /// The collider of slot 2, or a null reference when the slot is unused.
        /// </summary>
        public ColliderRef2D C2;

        /// <summary>
        /// The collider of slot 3, or a null reference when the slot is unused.
        /// </summary>
        public ColliderRef2D C3;
    }
}
