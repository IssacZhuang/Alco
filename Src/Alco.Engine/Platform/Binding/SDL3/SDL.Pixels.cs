// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using System.Runtime.InteropServices;
using static SDL3.SDL3;
using static SDL3.SDL_bool;

namespace SDL3;

unsafe partial class SDL3
{
    /// <summary>Creates a pixel format from a four-character code.</summary>
    /// <param name="A">The first byte.</param>
    /// <param name="B">The second byte.</param>
    /// <param name="C">The third byte.</param>
    /// <param name="D">The fourth byte.</param>
    /// <returns>The pixel format.</returns>
    public static SDL_PixelFormat SDL_DEFINE_PIXELFOURCC(byte A, byte B, byte C, byte D) => (SDL_PixelFormat)SDL_FOURCC(A, B, C, D);

    /// <summary>Creates a pixel format from its component fields.</summary>
    /// <param name="type">The pixel type.</param>
    /// <param name="order">The packed order.</param>
    /// <param name="layout">The packed layout.</param>
    /// <param name="bits">The bits per pixel.</param>
    /// <param name="bytes">The bytes per pixel.</param>
    /// <returns>The pixel format.</returns>
    public static SDL_PixelFormat SDL_DEFINE_PIXELFORMAT(int type, int order, int layout, int bits, int bytes)
        => (SDL_PixelFormat)((1 << 28) | ((type) << 24) | ((order) << 20) | ((layout) << 16) |
                             ((bits) << 8) | ((bytes) << 0));

    /// <summary>Returns the flag bits of a pixel format.</summary>
    /// <param name="X">The pixel format value.</param>
    /// <returns>The flag bits.</returns>
    public static byte SDL_PIXELFLAG(uint X)
    {
        return (byte)((X >> 28) & 0x0F);
    }

    /// <summary>Returns the pixel type of a pixel format.</summary>
    /// <param name="X">The pixel format.</param>
    /// <returns>The pixel type.</returns>
    public static SDL_PixelType SDL_PIXELTYPE(SDL_PixelFormat X)
    {
        return (SDL_PixelType)(((uint)X >> 24) & 0x0F);
    }

    /// <summary>Returns the packed order of a pixel format.</summary>
    /// <param name="X">The pixel format.</param>
    /// <returns>The packed order.</returns>
    public static SDL_PackedOrder SDL_PIXELORDER(SDL_PixelFormat X)
    {
        return (SDL_PackedOrder)(((uint)X >> 20) & 0x0F);
    }

    /// <summary>Returns the packed layout of a pixel format.</summary>
    /// <param name="X">The pixel format.</param>
    /// <returns>The packed layout.</returns>
    public static SDL_PackedLayout SDL_PIXELLAYOUT(SDL_PixelFormat X)
    {
        return (SDL_PackedLayout)(((uint)X >> 16) & 0x0F);
    }

    /// <summary>Returns the bits per pixel of a pixel format.</summary>
    /// <param name="X">The pixel format value.</param>
    /// <returns>The bits per pixel.</returns>
    public static byte SDL_BITSPERPIXEL(uint X)
    {
        return (byte)((X >> 8) & 0xFF);
    }

    /// <summary>Returns the bytes per pixel of a pixel format.</summary>
    /// <param name="X">The pixel format.</param>
    /// <returns>The bytes per pixel.</returns>
    public static byte SDL_BYTESPERPIXEL(SDL_PixelFormat X)
    {
        if (SDL_ISPIXELFORMAT_FOURCC(X))
        {
            if ((X == SDL_PixelFormat.Yuy2) || (X == SDL_PixelFormat.Uyvy) || (X == SDL_PixelFormat.Yvyu))
            {
                return 2;
            }
            return 1;
        }
        return (byte)((uint)X & 0xFF);
    }

    /// <summary>Checks whether a pixel format is indexed.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>True when the format is indexed.</returns>
    public static bool SDL_ISPIXELFORMAT_INDEXED(SDL_PixelFormat format)
    {
        if (SDL_ISPIXELFORMAT_FOURCC(format))
        {
            return false;
        }
        SDL_PixelType pType = SDL_PIXELTYPE(format);
        return (
            pType == SDL_PixelType.Index1 ||
            pType == SDL_PixelType.Index4 ||
            pType == SDL_PixelType.Index8
        );
    }

    /// <summary>Checks whether a pixel format is packed.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>True when the format is packed.</returns>
    public static bool SDL_ISPIXELFORMAT_PACKED(SDL_PixelFormat format)
    {
        if (SDL_ISPIXELFORMAT_FOURCC(format))
        {
            return false;
        }
        SDL_PixelType pType = SDL_PIXELTYPE(format);
        return (
            pType == SDL_PixelType.Packed8 ||
            pType == SDL_PixelType.Packed16 ||
            pType == SDL_PixelType.Packed32
        );
    }

    /// <summary>Checks whether a pixel format is an array format.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>True when the format is an array format.</returns>
    public static bool SDL_ISPIXELFORMAT_ARRAY(SDL_PixelFormat format)
    {
        if (SDL_ISPIXELFORMAT_FOURCC(format))
        {
            return false;
        }

        SDL_PixelType pType = SDL_PIXELTYPE(format);
        return (
            pType == SDL_PixelType.ArrayU8 ||
            pType == SDL_PixelType.ArrayU16 ||
            pType == SDL_PixelType.ArrayU32 ||
            pType == SDL_PixelType.ArrayF16 ||
            pType == SDL_PixelType.ArrayF32
        );
    }

    /// <summary>Checks whether a pixel format has an alpha channel.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>True when the format has an alpha channel.</returns>
    public static bool SDL_ISPIXELFORMAT_ALPHA(SDL_PixelFormat format)
    {
        if (!SDL_ISPIXELFORMAT_PACKED(format))
            return false;

        SDL_PackedOrder pOrder = SDL_PIXELORDER(format);
        return (
            pOrder == SDL_PackedOrder.Argb ||
            pOrder == SDL_PackedOrder.Rgba ||
            pOrder == SDL_PackedOrder.Abgr ||
            pOrder == SDL_PackedOrder.Bgra
        );
    }

    /// <summary>Checks whether a pixel format is a 10-bit packed format.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>True when the format is 10-bit packed.</returns>
    public static bool SDL_ISPIXELFORMAT_10BIT(SDL_PixelFormat format)
    {
        if (SDL_PIXELTYPE(format) == SDL_PixelType.Packed32 && SDL_PIXELLAYOUT(format) == SDL_PackedLayout._2101010)
            return true;

        return false;
    }

    /// <summary>Checks whether a pixel format is a four-character-code format.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>True when the format is a four-character code.</returns>
    public static bool SDL_ISPIXELFORMAT_FOURCC(SDL_PixelFormat format)
    {
        return (format == SDL_PixelFormat.Unknown) && (SDL_PIXELFLAG((uint)format) != 1);
    }

    /// <summary>Creates a colorspace value from its component fields.</summary>
    /// <param name="type">The color type.</param>
    /// <param name="range">The color range.</param>
    /// <param name="primaries">The color primaries.</param>
    /// <param name="transfer">The transfer characteristics.</param>
    /// <param name="matrix">The matrix coefficients.</param>
    /// <param name="chroma">The chroma location.</param>
    /// <returns>The encoded colorspace value.</returns>
    public static uint SDL_DEFINE_COLORSPACE(
        SDL_ColorType type,
        SDL_ColorRange range,
        SDL_ColorPrimaries primaries,
        SDL_TransferCharacteristics transfer,
        SDL_MatrixCoefficients matrix,
        SDL_ChromaLocation chroma
        )
    {
        return (((uint)(type) << 28) | ((uint)(range) << 24) | ((uint)(chroma) << 20) | ((uint)(primaries) << 10) | ((uint)(transfer) << 5) | ((uint)(matrix) << 0));
    }

    /// <summary>Returns the color type of a colorspace value.</summary>
    /// <param name="space">The encoded colorspace value.</param>
    /// <returns>The color type.</returns>
    public static SDL_ColorType SDL_COLORSPACETYPE(uint space)
    {
        return (SDL_ColorType)(((space) >> 28) & 0x0F);
    }

    /// <summary>Returns the color range of a colorspace value.</summary>
    /// <param name="space">The encoded colorspace value.</param>
    /// <returns>The color range.</returns>
    public static SDL_ColorRange SDL_COLORSPACERANGE(uint space)
    {
        return (SDL_ColorRange)(((space) >> 24) & 0x0F);
    }

    /// <summary>Returns the chroma location of a colorspace value.</summary>
    /// <param name="space">The encoded colorspace value.</param>
    /// <returns>The chroma location.</returns>
    public static SDL_ChromaLocation SDL_COLORSPACECHROMA(uint space)
    {
        return (SDL_ChromaLocation)(((space) >> 20) & 0x0F);
    }

    /// <summary>Returns the color primaries of a colorspace value.</summary>
    /// <param name="space">The encoded colorspace value.</param>
    /// <returns>The color primaries.</returns>
    public static SDL_ColorPrimaries SDL_COLORSPACEPRIMARIES(uint space)
    {
        return (SDL_ColorPrimaries)(((space) >> 10) & 0x1F);
    }

    /// <summary>Returns the transfer characteristics of a colorspace value.</summary>
    /// <param name="space">The encoded colorspace value.</param>
    /// <returns>The transfer characteristics.</returns>
    public static SDL_TransferCharacteristics SDL_COLORSPACETRANSFER(uint space)
    {
        return (SDL_TransferCharacteristics)(((space) >> 5) & 0x1F);
    }

    /// <summary>Returns the matrix coefficients of a colorspace value.</summary>
    /// <param name="space">The encoded colorspace value.</param>
    /// <returns>The matrix coefficients.</returns>
    public static SDL_MatrixCoefficients SDL_COLORSPACEMATRIX(uint space)
    {
        return (SDL_MatrixCoefficients)((space) & 0x1F);
    }

    /// <summary>Checks whether a colorspace uses BT.601 YUV coefficients.</summary>
    /// <param name="space">The encoded colorspace value.</param>
    /// <returns>True when the colorspace uses BT.601 coefficients.</returns>
    public static bool SDL_ISCOLORSPACE_YUV_BT601(uint space)
    {
        return (SDL_COLORSPACEMATRIX(space) == SDL_MatrixCoefficients.Bt601 || SDL_COLORSPACEMATRIX(space) == SDL_MatrixCoefficients.Bt470bg);
    }

    /// <summary>Checks whether a colorspace uses BT.709 YUV coefficients.</summary>
    /// <param name="space">The encoded colorspace value.</param>
    /// <returns>True when the colorspace uses BT.709 coefficients.</returns>
    public static bool SDL_ISCOLORSPACE_YUV_BT709(uint space)
    {
        return SDL_COLORSPACEMATRIX(space) == SDL_MatrixCoefficients.Bt709;
    }

    /// <summary>Checks whether a colorspace uses limited range.</summary>
    /// <param name="space">The encoded colorspace value.</param>
    /// <returns>True when the colorspace is limited range.</returns>
    public static bool SDL_ISCOLORSPACE_LIMITED_RANGE(uint space)
    {
        return SDL_COLORSPACERANGE(space) == SDL_ColorRange.Limited;
    }

    /// <summary>Checks whether a colorspace uses full range.</summary>
    /// <param name="space">The encoded colorspace value.</param>
    /// <returns>True when the colorspace is full range.</returns>
    public static bool SDL_ISCOLORSPACE_FULL_RANGE(uint space)
    {
        return SDL_COLORSPACERANGE(space) == SDL_ColorRange.Full;
    }

    /// <summary>Creates a surface that references pixel data in an array.</summary>
    /// <param name="source">The pixel data.</param>
    /// <param name="width">The surface width in pixels.</param>
    /// <param name="height">The surface height in pixels.</param>
    /// <param name="pitch">The number of bytes per row.</param>
    /// <param name="format">The pixel format.</param>
    /// <returns>The created surface, or null on failure.</returns>
    public static SDL_Surface* SDL_CreateSurfaceFrom<T>(T[] source, int width, int height, int pitch, SDL_PixelFormat format)
        where T : unmanaged
    {
        ReadOnlySpan<T> span = source.AsSpan();

        return SDL_CreateSurfaceFrom(span, width, height, format, pitch);
    }

    /// <summary>Creates a surface that references pixel data in a span.</summary>
    /// <param name="source">The pixel data.</param>
    /// <param name="width">The surface width in pixels.</param>
    /// <param name="height">The surface height in pixels.</param>
    /// <param name="format">The pixel format.</param>
    /// <param name="pitch">The number of bytes per row.</param>
    /// <returns>The created surface, or null on failure.</returns>
    public static SDL_Surface* SDL_CreateSurfaceFrom<T>(ReadOnlySpan<T> source, int width, int height, SDL_PixelFormat format, int pitch)
        where T : unmanaged
    {
        return SDL_CreateSurfaceFrom(ref MemoryMarshal.GetReference(source), width, height, format, pitch);
    }

    /// <summary>Creates a surface that references pixel data starting at a reference.</summary>
    /// <param name="source">The first pixel of the data.</param>
    /// <param name="width">The surface width in pixels.</param>
    /// <param name="height">The surface height in pixels.</param>
    /// <param name="format">The pixel format.</param>
    /// <param name="pitch">The number of bytes per row.</param>
    /// <returns>The created surface, or null on failure.</returns>
    public static SDL_Surface* SDL_CreateSurfaceFrom<T>(ref T source, int width, int height, SDL_PixelFormat format, int pitch)
         where T : unmanaged
    {
        fixed (void* sourcePointer = &source)
        {
            return SDL_CreateSurfaceFrom(width, height, format, (nint)sourcePointer, pitch);
        }
    }
}
