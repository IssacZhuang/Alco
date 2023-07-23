using System;


namespace Vocore
{
    public unsafe struct TileTerrainBlock : IDisposable
    {
        public const int Width = 64;
        public const int Height = 64;
        private NativeBuffer<TileTerrainData> _data;
        public TileTerrainBlock(TileTerrainData defaultValue = default(TileTerrainData))
        {
            _data = new NativeBuffer<TileTerrainData>(Width * Height);
        }

        public void Dispose()
        {
            _data.Dispose();
        }

        public TileTerrainData this[int x, int y]
        {
            get
            {
                if (InRange(x, y))
                {
                    return _data[x + y * Width];
                }
                return default(TileTerrainData);
            }
            set
            {
                if (InRange(x, y))
                {
                    _data[x + y * Width] = value;
                }
            }
        }


        public bool InRange(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }
    }
}