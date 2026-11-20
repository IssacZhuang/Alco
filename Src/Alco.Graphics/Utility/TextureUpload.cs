// Texture upload sizing, caller-recorded staging copies, and fixed-capacity queue tiling.

using System.Diagnostics;

using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>Uploads CPU texture data through caller-owned staging or a reusable staging queue.</summary>
public static unsafe class TextureUpload
{
    /// <summary>Returns the tightly packed byte size of one selected texture region.</summary>
    /// <param name="description">The texture description; combined depth/stencil formats are unsupported.</param>
    /// <param name="copy">The selected region, with whole mip subresources for depth/stencil; source pitches do not affect the result.</param>
    /// <returns>The region's tightly packed size in bytes.</returns>
    public static ulong GetSizeBytes(in TextureDesc description, in TextureCopyDesc copy = default)
    {
        TextureFormatInfo format = Gpu.GetTextureFormatInfo(description.Format);
        Extent3D blocks = GetRegionBlocks(description, copy, format, out _);
        return (ulong)blocks.X * blocks.Y * blocks.Z * format.BytesPerBlock;
    }

    /// <summary>Copies CPU data immediately into staging and records one tightly packed texture upload; synchronization remains caller-owned.</summary>
    /// <param name="commands">The command buffer recording the upload outside render passes.</param>
    /// <param name="staging">Buffer-backed writable mapped storage, kept unchanged and alive until the recorded upload completes.</param>
    /// <param name="destination">The initialized destination texture, matching the description.</param>
    /// <param name="description">The destination texture description; combined depth/stencil formats are unsupported.</param>
    /// <param name="source">The CPU region data with the selected pitches; final row and slice padding is not required.</param>
    /// <param name="copy">The selected region and valid source pitches; depth/stencil uploads require whole mip subresources.</param>
    /// <exception cref="InvalidOperationException">Staging cannot fit the tightly packed region; no data is copied or commands recorded.</exception>
    public static void UploadTexture(GPUCommandBuffer commands, GPUAllocation<byte> staging, GPUTexture destination, in TextureDesc description,
        ReadOnlySpan<byte> source, in TextureCopyDesc copy = default)
    {
        TextureFormatInfo format = Gpu.GetTextureFormatInfo(description.Format);
        Extent3D blocks = GetRegionBlocks(description, copy, format, out TextureCopyDesc region);
        ulong rowBytes = (ulong)blocks.X * format.BytesPerBlock;
        ulong sizeBytes = rowBytes * blocks.Y * blocks.Z;
        if (sizeBytes > staging.SizeBytes)
            throw new InvalidOperationException($"Texture upload requires {sizeBytes} bytes of staging capacity, " +
                $"but the allocation has {staging.SizeBytes} bytes.");
        ulong rowPitch = copy.RowPitchBytes != 0 ? copy.RowPitchBytes : rowBytes;
        ulong slicePitch = copy.SlicePitchBytes != 0 ? copy.SlicePitchBytes : rowPitch * blocks.Y;
        Debug.Assert(staging.Cpu != null && staging.Buffer != null && !source.IsEmpty);
        Debug.Assert(rowPitch >= rowBytes && slicePitch >= rowPitch * blocks.Y);
        Debug.Assert((ulong)source.Length >= (blocks.Z - 1) * slicePitch + (blocks.Y - 1) * rowPitch + rowBytes);
        fixed (byte* input = source)
            CopyRows(input, staging.Cpu, rowBytes, blocks.Y, blocks.Z, rowPitch, slicePitch);
        commands.CopyMemoryToTexture(staging.GPURange.Slice(0, sizeBytes), destination, region);
    }

    /// <summary>Tiles color uploads or uploads whole depth/stencil mip subresources; combined depth/stencil formats are unsupported.</summary>
    /// <param name="queue">The staging queue, with capacity for one copy-granularity tile or one complete depth/stencil mip subresource.</param>
    /// <param name="destination">The destination texture, matching the description.</param>
    /// <param name="description">The destination texture description.</param>
    /// <param name="source">The CPU region data with the selected pitches; trailing padding after the final row and slice is not required.</param>
    /// <param name="copy">The selected region and valid memory pitches; depth/stencil texture origins must be zero.</param>
    /// <exception cref="InvalidOperationException">The queue capacity cannot fit the smallest legal copy; no upload is staged or recorded.</exception>
    public static void UploadTexture(UploadQueue queue, GPUTexture destination, in TextureDesc description,
        ReadOnlySpan<byte> source, in TextureCopyDesc copy = default)
    {
        ulong capacity = queue.Stats.Capacity;
        TextureFormatInfo format = Gpu.GetTextureFormatInfo(description.Format);
        Extent3D blocks = GetRegionBlocks(description, copy, format, out TextureCopyDesc region);
        bool volume = description.Type == TextureType.ThreeD;
        uint slices = blocks.Z;
        uint columns = blocks.X;
        uint rows = blocks.Y;
        ulong rowBytes = (ulong)columns * format.BytesPerBlock;
        ulong rowPitch = copy.RowPitchBytes != 0 ? copy.RowPitchBytes : rowBytes;
        ulong slicePitch = copy.SlicePitchBytes != 0 ? copy.SlicePitchBytes : rowPitch * rows;
        Debug.Assert(region.Extent.X != 0 && region.Extent.Y != 0 && slices != 0 && !source.IsEmpty);
        Debug.Assert(rowPitch >= rowBytes && slicePitch >= rowPitch * rows);
        Debug.Assert((ulong)source.Length >= (slices - 1) * slicePitch + (rows - 1) * rowPitch + rowBytes);
        Extent3D granularity = queue._state.TextureGranularity;
        if (!volume) granularity.Z = 1;
        if (format.Depth || format.Stencil) granularity = new Extent3D(columns, rows, volume ? slices : 1);
        Extent3D tile = TextureUploadTile(new Extent3D(columns, rows, slices), granularity, format.BytesPerBlock, capacity);

        fixed (byte* sourceData = source)
        {
            for (uint slice = 0; slice < slices; slice += tile.Z)
            {
                TextureCopyDesc part = region;
                part.BaseSlice = volume ? 0 : region.BaseSlice + slice;
                part.Offset.Z = volume ? region.Offset.Z + slice : 0;
                uint partSlices = tile.Z < slices - slice ? tile.Z : slices - slice;
                part.Extent.Z = volume ? partSlices : 1;
                part.SliceCount = volume ? 1 : partSlices;
                for (uint row = 0; row < rows; row += tile.Y)
                {
                    part.Offset.Y = region.Offset.Y + row * format.BlockExtent.Y;
                    uint partRows = tile.Y < rows - row ? tile.Y : rows - row;
                    part.Extent.Y = partRows * format.BlockExtent.Y;
                    if (part.Extent.Y > region.Extent.Y - row * format.BlockExtent.Y) part.Extent.Y = region.Extent.Y - row * format.BlockExtent.Y;
                    for (uint column = 0; column < columns; column += tile.X)
                    {
                        uint partColumns = tile.X < columns - column ? tile.X : columns - column;
                        part.Offset.X = region.Offset.X + column * format.BlockExtent.X;
                        part.Extent.X = partColumns * format.BlockExtent.X;
                        if (part.Extent.X > region.Extent.X - column * format.BlockExtent.X) part.Extent.X = region.Extent.X - column * format.BlockExtent.X;
                        ulong packedRowBytes = (ulong)partColumns * format.BytesPerBlock;
                        ulong packedSliceBytes = packedRowBytes * partRows;
                        GPUAllocation<byte> staging = queue.Reserve(packedSliceBytes * partSlices);
                        byte* input = sourceData + slice * slicePitch + row * rowPitch + (ulong)column * format.BytesPerBlock;
                        CopyRows(input, staging.Cpu, packedRowBytes, partRows, partSlices, rowPitch, slicePitch);
                        queue.Begin().CopyMemoryToTexture(staging.GPURange, destination, part);
                        ++queue._state.OperationCount;
                    }
                }
            }
        }
    }

    internal static Extent3D GetRegionBlocks(in TextureDesc description, in TextureCopyDesc copy, TextureFormatInfo format, out TextureCopyDesc region)
    {
        Debug.Assert(copy.MipLevel < description.MipLevels && format.BytesPerBlock != 0 && !(format.Depth && format.Stencil));
        uint mipWidth = Math.Max(1u, description.Extent.X >> (int)copy.MipLevel);
        uint mipHeight = Math.Max(1u, description.Extent.Y >> (int)copy.MipLevel);
        uint mipDepth = Math.Max(1u, description.Extent.Z >> (int)copy.MipLevel);
        region = copy;
        if (region.Extent.X == 0) region.Extent.X = mipWidth - region.Offset.X;
        if (region.Extent.Y == 0) region.Extent.Y = mipHeight - region.Offset.Y;
        if (region.Extent.Z == 0) region.Extent.Z = mipDepth - region.Offset.Z;
        if (region.SliceCount == 0) region.SliceCount = description.LayerCount - region.BaseSlice;
        region.RowPitchBytes = 0;
        region.SlicePitchBytes = 0;
        uint slices = description.Type == TextureType.ThreeD ? region.Extent.Z : region.SliceCount;
        Debug.Assert(region.Extent.X != 0 && region.Extent.Y != 0 && slices != 0);
        if (format.Depth || format.Stencil)
        {
            Debug.Assert(region.Offset.X == 0 && region.Offset.Y == 0 && region.Offset.Z == 0);
            Debug.Assert(region.Extent.X == mipWidth && region.Extent.Y == mipHeight && region.Extent.Z == mipDepth);
        }
        return new Extent3D
        {
            X = (region.Extent.X + format.BlockExtent.X - 1) / format.BlockExtent.X,
            Y = (region.Extent.Y + format.BlockExtent.Y - 1) / format.BlockExtent.Y,
            Z = slices,
        };
    }

    private static void CopyRows(byte* input, byte* output, ulong rowBytes, uint rows, uint slices, ulong rowPitch, ulong slicePitch)
    {
        ulong sliceBytes = rowBytes * rows;
        if (rowPitch == rowBytes && slicePitch == sliceBytes)
        {
            ulong sizeBytes = sliceBytes * slices;
            System.Buffer.MemoryCopy(input, output, (long)sizeBytes, (long)sizeBytes);
            return;
        }
        for (uint z = 0; z < slices; ++z)
        {
            byte* inputSlice = input + z * slicePitch;
            byte* outputSlice = output + z * sliceBytes;
            if (rowPitch == rowBytes)
                System.Buffer.MemoryCopy(inputSlice, outputSlice, (long)sliceBytes, (long)sliceBytes);
            else
                for (uint y = 0; y < rows; ++y)
                    System.Buffer.MemoryCopy(inputSlice + y * rowPitch, outputSlice + y * rowBytes, (long)rowBytes, (long)rowBytes);
        }
    }

    private static Extent3D TextureUploadTile(Extent3D blocks, Extent3D granularity, uint bytesPerBlock, ulong capacity)
    {
        if (granularity.X == 0) granularity.X = blocks.X;
        if (granularity.Y == 0) granularity.Y = blocks.Y;
        if (granularity.Z == 0) granularity.Z = blocks.Z;
        Extent3D minimum = new()
        {
            X = granularity.X < blocks.X ? granularity.X : blocks.X,
            Y = granularity.Y < blocks.Y ? granularity.Y : blocks.Y,
            Z = granularity.Z < blocks.Z ? granularity.Z : blocks.Z,
        };
        ulong minimumBytes = (ulong)minimum.X * minimum.Y * minimum.Z * bytesPerBlock;
        if (capacity < minimumBytes)
            throw new InvalidOperationException($"Texture upload requires at least {minimumBytes} bytes of staging capacity for one legal copy, " +
                $"but the queue has {capacity} bytes.");
        Extent3D tile = default;
        ulong limit = capacity / ((ulong)minimum.Y * minimum.Z * bytesPerBlock);
        tile.X = limit < blocks.X ? (uint)limit : blocks.X;
        if (tile.X < blocks.X) tile.X -= tile.X % granularity.X;
        limit = capacity / ((ulong)tile.X * minimum.Z * bytesPerBlock);
        tile.Y = limit < blocks.Y ? (uint)limit : blocks.Y;
        if (tile.Y < blocks.Y) tile.Y -= tile.Y % granularity.Y;
        limit = capacity / ((ulong)tile.X * tile.Y * bytesPerBlock);
        tile.Z = limit < blocks.Z ? (uint)limit : blocks.Z;
        if (tile.Z < blocks.Z) tile.Z -= tile.Z % granularity.Z;
        return tile;
    }
}
