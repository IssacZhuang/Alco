using System;
using System.Numerics;

namespace Vocore
{
    public struct AtlasPackResult
    {
        public uint width;
        public uint height;
        public RectInt[] uvRects;
        public int numOutside;
    }

    public static class AlgoAtlasPacking{
        private struct AtlasSize
        {
            public AtlasSize(uint width, uint height)
            {
                this.width = width;
                this.height = height;
            }
            public uint width;
            public uint height;
        }
        private static readonly AtlasSize[] AltasSize = new AtlasSize[]
        {
            new AtlasSize(16,16),
            new AtlasSize(32,16),
            new AtlasSize(32,32),
            new AtlasSize(64,32),
            new AtlasSize(64,64),
            new AtlasSize(128,64),
            new AtlasSize(128,128),
            new AtlasSize(256,128),
            new AtlasSize(256,256),
            new AtlasSize(512,256),
            new AtlasSize(512,512),
            new AtlasSize(1024,512),
            new AtlasSize(1024,1024),
            new AtlasSize(2048,1024),
            new AtlasSize(2048,2048),
            new AtlasSize(4096,2048),
            new AtlasSize(4096,4096),// max
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
            AtlasSize bestSize = AltasSize[0];
            int countPerRow = 1;
            int countPerCol = 1;
            for (int i = 0; i < AltasSize.Length; i++)
            {
                AtlasSize size = AltasSize[i];
                countPerRow = (int)(size.width / (widthPerTile + padding));
                countPerCol = (int)(size.height / (heightPerTile + padding));

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
                width = bestSize.width,
                height = bestSize.height,
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