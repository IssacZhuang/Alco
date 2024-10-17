using System.Runtime.CompilerServices;

namespace NVorbis.Contracts
{
    struct HuffmanListNode
    {
        public static readonly HuffmanListNode Null = new HuffmanListNode
        {
            Value = -1,
            Length = 0,
            Bits = 0,
            Mask = 0
        };


        internal bool NotNull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Value != -1;
        }

        internal int Value;

        internal int Length;
        internal int Bits;
        internal int Mask;
    }
}
