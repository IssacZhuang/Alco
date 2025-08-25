using System;
using SharpMSDF.Core;

namespace SharpMSDF.Atlas
{
    internal static class Blitter
    {
        public static void Blit<T>(BitmapRef<T> dst, BitmapConstRef<T> src, int dx, int dy, int sx, int sy, int w, int h) where T : struct
        {
            BoundArea(dst, src, ref dx, ref dy, ref sx, ref sy, ref w, ref h);
            if (w == 0 || h == 0)
                return;

            for (int y = 0; y < h; ++y)
            {
                int dstRow = dy + y;
                int srcRow = sy + y;
                for (int x = 0; x < w; ++x)
                {
                    for (int c = 0; c < dst.N; ++c)
                        dst[dx + x, dstRow, c] = src[sx + x, srcRow, c];
                }
            }
        }

        public static void Blit(BitmapRef<byte> dst, BitmapConstRef<float> src, int dx, int dy, int sx, int sy, int w, int h)
        {
            BoundArea(dst, src, ref dx, ref dy, ref sx, ref sy, ref w, ref h);
            if (w == 0 || h == 0)
                return;

            for (int y = 0; y < h; ++y)
            {
                int dstRow = dy + y;
                int srcRow = sy + y;
                for (int x = 0; x < w; ++x)
                {
                    for (int c = 0; c < dst.N; ++c)
                        dst[dx + x, dstRow, c] = PixelFloatToByte(src[sx + x, srcRow, c]);
                }
            }
        }

        private static void BoundArea<TSrc, TDest>(BitmapRef<TDest> dst, BitmapConstRef<TSrc> src,
            ref int dx, ref int dy, ref int sx, ref int sy, ref int w, ref int h)
            where TSrc : struct where TDest : struct
        {
            if (dx < 0) { w += dx; sx -= dx; dx = 0; }
            if (dy < 0) { h += dy; sy -= dy; dy = 0; }
            if (sx < 0) { w += sx; dx -= sx; sx = 0; }
            if (sy < 0) { h += sy; dy -= sy; sy = 0; }

            int maxW = Math.Min(dst.OriginalWidth - dx, src.OriginalWidth - sx);
            int maxH = Math.Min(dst.OriginalHeight - dy, src.OriginalHeight - sy);
            w = Math.Max(0, Math.Min(w, maxW));
            h = Math.Max(0, Math.Min(h, maxH));
        }

        private static byte PixelFloatToByte(float value)
        {
            return (byte)Math.Clamp((int)(value * 255.0f + 0.5f), 0, 255);
        }
    }
}
