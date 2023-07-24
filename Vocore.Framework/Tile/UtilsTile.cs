using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;


namespace Vocore
{
    public static class UtilsTile
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetTileBlockIndex(int tileBlockX, int tileBlockY)
        {
            return tileBlockX + tileBlockY * TileMap.MaxTileBlockWidth;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetTileBlockIndex(int2 tileBlockPos)
        {
            return GetTileBlockIndex(tileBlockPos.x, tileBlockPos.y);
        }

        

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 GetLocalPosInTileBlock(int worldSpacePosX, int worldSpacePosY)
        {
            return new int2(worldSpacePosX % TileBlock.Width, worldSpacePosY % TileBlock.Height);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 GetLocalPosInTileBlock(int2 pos)
        {
            return GetLocalPosInTileBlock(pos.x, pos.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 GetTileBlockPos(int worldSpacePosX, int worldSpacePosY)
        {
            return new int2(worldSpacePosX / TileBlock.Width, worldSpacePosY / TileBlock.Height);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 GetTileBlockPos(int2 worldSpacePosition)
        {
            return GetTileBlockPos(worldSpacePosition.x, worldSpacePosition.y);
        }

        

    }
}
