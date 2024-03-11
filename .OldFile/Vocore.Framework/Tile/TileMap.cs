using System;


namespace Vocore
{
    public class TileMap
    {
        public const int MaxTileBlockWidth = 256;
        public const int MaxTileBlockHeight = 256;
        private TileBlock[] _blocks;
        private int2 _globalPositionOffset;
        public int2 GlobalPositionOffset
        {
            get => _globalPositionOffset;
            set => _globalPositionOffset = value;
        }
        public TileMap()
        {
            _blocks = new TileBlock[MaxTileBlockWidth * MaxTileBlockHeight];
        }

        public TileData GetTile(int2 worldSpacePosition)
        {
            worldSpacePosition = ToLocalPosition(worldSpacePosition);
            TileBlock block = GetTileBlockByPosition(worldSpacePosition);
            int2 localPos = UtilsTile.GetLocalPosInTileBlock(worldSpacePosition);
            return block[localPos.x, localPos.y];
        }

        public void SetTile(int2 worldSpacePosition, TileData tileData)
        {
            worldSpacePosition = ToLocalPosition(worldSpacePosition);
            TileBlock block = GetTileBlockByPosition(worldSpacePosition);
            int2 localPos = UtilsTile.GetLocalPosInTileBlock(worldSpacePosition);
            block[localPos.x, localPos.y] = tileData;
        }

        public void Draw(AABBInt worldSpaceBound)
        {
            worldSpaceBound.min -= _globalPositionOffset;
            worldSpaceBound.max -= _globalPositionOffset;

            int2 tileBlockMin = UtilsTile.GetTileBlockPos(worldSpaceBound.min);
            int2 tileBlockMax = UtilsTile.GetTileBlockPos(worldSpaceBound.max);

            
        }

        private int2 ToLocalPosition(int2 worldSpacePosition)
        {
            return worldSpacePosition - _globalPositionOffset;
        }

        private TileBlock GetTileBlockByPosition(int2 pos)
        {
            pos -= _globalPositionOffset;
            int2 tileBlockPos = UtilsTile.GetTileBlockPos(pos);
            return GetTileBlockInternal(tileBlockPos.x, tileBlockPos.y);
        }

        private TileBlock GetTileBlockInternal(int x, int y)
        {
            int index = UtilsTile.GetTileBlockIndex(x, y);
            if (InRange(index))
            {
                return _blocks[index];
            }
            return default(TileBlock);
        }

        private bool InRange(int index)
        {
            return index >= 0 && index < MaxTileBlockWidth * MaxTileBlockHeight;
        }

    }
}
