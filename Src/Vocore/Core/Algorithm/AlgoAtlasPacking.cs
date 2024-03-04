using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vocore
{
    public struct AtlasPackResult
    {
        public int width;
        public int height;
        public RectInt[] uvRects;
        public int numOutside;
    }

    public static class AlgoAtlasPacking{

        private static readonly int2[] AltasSize = new int2[]
        {
            new int2(16,16),
            new int2(32,16),
            new int2(32,32),
            new int2(64,32),
            new int2(64,64),
            new int2(128,64),
            new int2(128,128),
            new int2(256,128),
            new int2(256,256),
            new int2(512,256),
            new int2(512,512),
            new int2(1024,512),
            new int2(1024,1024),
            new int2(2048,1024),
            new int2(2048,2048),
            new int2(4096,2048),
            new int2(4096,4096),// max
        };
        public static AtlasPackResult PackTiled(int widthPerTile, int heightPerTile, int count, int padding = 0)
        {
            if (count <= 0)
            {
                throw new ArgumentException("count must be greater than 0");
            }

            if (widthPerTile <= 0 || heightPerTile <= 0)
            {
                throw new ArgumentException("widthPerTile and heightPerTile must be greater than 0");
            }

            if (padding < 0)
            {
                throw new ArgumentException("padding must be greater than or equal to 0");
            }

            //find the best size
            int2 bestSize = AltasSize[0];
            int countPerRow = 1;
            int countPerCol = 1;
            for (int i = 0; i < AltasSize.Length; i++)
            {
                int2 size = AltasSize[i];
                countPerRow = size.x / (widthPerTile + padding);
                countPerCol = size.y / (heightPerTile + padding);

                bestSize = size;
                if (countPerRow * countPerCol >= count)
                {
                    break;
                }
            }

            int numPacked = countPerRow * countPerCol;

            //pack
            AtlasPackResult result = new AtlasPackResult
            {
                width = bestSize.x,
                height = bestSize.y,
                uvRects = new RectInt[numPacked],
                numOutside = count - numPacked
            };


            //the rects is uvRects, the origin is (0,0)
            for (int i = 0; i < numPacked; i++)
            {
                int indexInRow = i % countPerRow;
                int indexInCol = i / countPerRow;
                int x = indexInRow * (widthPerTile + padding);
                int y = indexInCol * (heightPerTile + padding);
                result.uvRects[i] = new RectInt(x, y, widthPerTile, heightPerTile);
            }

            return result;
        }
    }
}