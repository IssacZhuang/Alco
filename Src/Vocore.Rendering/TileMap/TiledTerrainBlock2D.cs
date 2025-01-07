using System.Runtime.InteropServices;

namespace Vocore.Rendering;

public class TiledTerrainBlock2D : AutoDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct TileData
    {
        public int X;
        public int Y;
        public uint SpriteIndex;
    }
    private readonly TextureAtlas _textureAtlas;
    private readonly GraphicsArrayBuffer<TileData> _tileDataBufferGPU;
    private readonly NativeBuffer<TileData> _tileDataBufferCPU;
    private readonly Material _material;


    internal TiledTerrainBlock2D(
        RenderingSystem renderingSystem,
        TextureAtlas textureAtlas,
        Material material,
        int width,
        int height)
    {
        _textureAtlas = textureAtlas;
        _tileDataBufferGPU = new GraphicsArrayBuffer<TileData>(renderingSystem, width * height, "TiledTerrainBlock2D_TileData");
        _tileDataBufferCPU = new NativeBuffer<TileData>(width * height);
        _material = material.CreateInstance();
    }



    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tileDataBufferGPU.Dispose();
        }

        _tileDataBufferCPU.Dispose();
    }
}
