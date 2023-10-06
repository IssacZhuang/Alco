using System;
using System.Runtime.CompilerServices;


using Vocore.Unsafe;


namespace Vocore
{
    public unsafe struct TileBlock
    {
        public const int Width = 64;
        public const int Height = 64;
        private TileData* _data;
        public bool HasData => _data != null;
        private AABBInt LocalAABB => new AABBInt(0, 0, Width - 1, Height - 1);

        public unsafe TileData* GetUnsafePtr()
        {
            return _data;
        }

        public void Initialze()
        {
            if (!HasData)
            {
                _data = (TileData*)UtilsMemory.Alloc(Width * Height * UtilsMemory.SizeOf<TileData>());
            }
        }

        public void Clear()
        {
            if (HasData)
            {
                UtilsMemory.Free(_data);
                _data = null;
            }
        }

        public TileData this[int x, int y]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (InRange(x, y))
                {
                    return _data[x + y * Width];
                }
                return default(TileData);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (InRange(x, y))
                {
                    _data[x + y * Width] = value;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InRange(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public void SetTile(int x, int y, TileData tile)
        {
            if (InRange(x, y))
            {
                _data[x + y * Width] = tile;
            }
        }

        public TileData GetTile(int x, int y)
        {
            if (InRange(x, y))
            {
                return _data[x + y * Width];
            }
            return default(TileData);
        }

        public void DrawTile(AABBInt aabb, int2 drawOffset)
        {
            AABBInt intersection = LocalAABB.Intersection(aabb);
            for (int y = intersection.min.y; y <= intersection.max.y; y++)
            {
                for (int x = intersection.min.x; x <= intersection.max.x; x++)
                {
                    DrawTileInternal(x, y, drawOffset.x, drawOffset.y);
                }
            }
        }

        private void DrawTileInternal(int localPosX, int localPosY, int drawOffsetX,int drawOffsetY)
        {

            //todo
        }
    }
}