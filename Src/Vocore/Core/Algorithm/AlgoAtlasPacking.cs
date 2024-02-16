using System;
using System.Numerics;

namespace Vocore
{
    public struct AtlasPackResult
    {
        public uint width;
        public uint height;
        public Rect[] uvRects;
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
        public static AtlasPackResult PackTiled(uint widthPerTile, uint heightPerTile, int count, uint padding = 0)
        {
            //find the best size
            AtlasSize bestSize = AltasSize[0];
            int countPerRow = 1;
            for (int i = 0; i < AltasSize.Length; i++)
            {
                AtlasSize size = AltasSize[i];
                countPerRow = (int)(size.width / (widthPerTile + padding));
                int countPerCol = (int)(size.height / (heightPerTile + padding));
                if (countPerRow * countPerCol >= count)
                {
                    bestSize = size;
                    break;
                }
            }

            //pack
            AtlasPackResult result = new AtlasPackResult
            {
                width = bestSize.width,
                height = bestSize.height,
                uvRects = new Rect[count]
            };

            float widthF = bestSize.width;
            float heightF = bestSize.height;

            float uvWidth = widthPerTile / widthF;
            float uvHeight = heightPerTile / heightF;

            //the rects is uvRects, the origin is (0,0)
            for (int i = 0; i < count; i++)
            {
                int indexInRow = i % countPerRow;
                int indexInCol = i / countPerRow;
                float x = indexInRow * (widthPerTile + padding) / widthF;
                float y = indexInCol * (heightPerTile + padding) / heightF;
                result.uvRects[i] = new Rect(x, y, uvWidth, uvHeight);
            }

            return result;
        }
    }
}