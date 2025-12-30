using System;
using System.IO;
using System.Runtime.CompilerServices;
using SharpMSDF.Core;

namespace SharpMSDF.IO
{
    public static class Bmp
    {
        static void WriteBmpHeader(BinaryWriter writer, int width, int height, out int paddedWidth)
        {
            paddedWidth = (3 * width + 3) & ~3;
            uint bitmapStart = 54;
            uint bitmapSize = (uint)(paddedWidth * height);
            uint fileSize = bitmapStart + bitmapSize;

            writer.Write((ushort)0x4D42);      // 'BM' signature
            writer.Write(fileSize);            // file size
            writer.Write((ushort)0);           // reserved 1
            writer.Write((ushort)0);           // reserved 2
            writer.Write(bitmapStart);         // pixel data offset

            writer.Write(40u);                 // DIB header size
            writer.Write(width);               // image width
            writer.Write(height);              // image height
            writer.Write((ushort)1);           // color planes
            writer.Write((ushort)24);          // bits per pixel
            writer.Write(0u);                  // compression
            writer.Write(bitmapSize);          // image size
            writer.Write(2835u);               // horizontal resolution (72 DPI)
            writer.Write(2835u);               // vertical resolution (72 DPI)
            writer.Write(0u);                  // number of colors in palette
            writer.Write(0u);                  // important colors
        }

        public static bool SaveBmp(BitmapConstRef<byte> bitmap, string filename)
        {
            using var file = File.Open(filename, FileMode.Create);
            using var writer = new BinaryWriter(file);

            WriteBmpHeader(writer, bitmap.SubWidth, bitmap.SubHeight, out int paddedWidth);

            int padLength = paddedWidth - 3 * bitmap.SubWidth;
            byte[] padding = new byte[4];

            byte r, g, b;
            for (int y = 0; y < bitmap.SubHeight; y++)
            {
                for (int x = 0; x < bitmap.SubWidth; x++)
                {
                    switch (bitmap.N)
                    {
                        case 1:
                            byte px = bitmap[x, y, 0];
                            writer.Write(px); writer.Write(px); writer.Write(px);
                            break;
                        case 3:
                            r = bitmap[x, y, 0];
                            g = bitmap[x, y, 1];
                            b = bitmap[x, y, 2];
                            writer.Write(r); writer.Write(g); writer.Write(b);
                            break;
                        case 4:
                            // Alpha is not supported
                            return false;
                    }
                }
                writer.Write(padding, 0, padLength);
            }

            return true;
        }
        public static bool SaveBmp(BitmapConstRef<float> bitmap, string filename)
        {
            using var file = File.Open(filename, FileMode.Create);
            using var writer = new BinaryWriter(file);

            WriteBmpHeader(writer, bitmap.SubWidth, bitmap.SubHeight, out int paddedWidth);

            int padLength = paddedWidth - 3 * bitmap.SubWidth;
            byte[] padding = new byte[4];

            byte r, g, b;
            for (int y = 0; y < bitmap.SubHeight; y++)
            {
                for (int x = 0; x < bitmap.SubWidth; x++)
                {
                    switch (bitmap.N)
                    {
                        case 1:
                            byte px = PixelFloatToByte(bitmap[x, y, 0]);
                            writer.Write(px); writer.Write(px); writer.Write(px);
                            break;
                        case 3:
                            r = PixelFloatToByte(bitmap[x, y, 0]);
                            g = PixelFloatToByte(bitmap[x, y, 1]);
                            b = PixelFloatToByte(bitmap[x, y, 2]);
                            writer.Write(r); writer.Write(g); writer.Write(b);
                            break;
                        case 4:
                            // Alpha is not supported
                            return false;
                    }
                }
                writer.Write(padding, 0, padLength);
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte PixelFloatToByte(float x)
        {
            float clamped = Arithmetic.Clamp(x); // clamps between 0.0 and 1.0
            return (byte)~(int)(255.5f - 255f * clamped);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float PixelByteToFloat(byte x)
        {
            return x * (1f / 255f);
        }
    }
}