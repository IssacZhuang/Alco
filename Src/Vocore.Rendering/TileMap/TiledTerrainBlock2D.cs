using System.Numerics;
using System.Runtime.InteropServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class TiledTerrainBlock2D : AutoDisposable
{
    //per block
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
    }

    //per sprite
    [StructLayout(LayoutKind.Sequential)]
    private struct SpriteData
    {
        public Rect UVRect;
        public float Scale;
    }

    //per tile
    [StructLayout(LayoutKind.Sequential)]
    private struct TileData
    {
        public ColorFloat Color;
        public int X;
        public int Y;
        public uint SpriteIndex;
    }
    private readonly TextureAtlas _textureAtlas;
    private readonly GraphicsArrayBuffer<TileData> _tileDataBuffer;
    private readonly GraphicsArrayBuffer<SpriteData> _spriteDataBuffer;
    private readonly Material _material;
    private readonly Mesh _mesh;

    public Transform3D Transform;


    internal TiledTerrainBlock2D(
        RenderingSystem renderingSystem,
        TextureAtlas textureAtlas,
        Material material,
        int width,
        int height)
    {
        _textureAtlas = textureAtlas;
        _tileDataBuffer = new GraphicsArrayBuffer<TileData>(renderingSystem, width * height, "TiledTerrainBlock2D_TileData");
        _spriteDataBuffer = new GraphicsArrayBuffer<SpriteData>(renderingSystem, textureAtlas.Sprites.Count, "TiledTerrainBlock2D_SpriteData");
        _material = material.CreateInstance();
        _mesh = renderingSystem.MeshSprite;

        for (int i = 0; i < textureAtlas.Sprites.Count; i++)
        {
            _spriteDataBuffer[i] = new SpriteData { UVRect = textureAtlas.Sprites[i].UVRect };
        }
        _spriteDataBuffer.UpdateBuffer();

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                _tileDataBuffer[i * height + j] = new TileData { X = i, Y = j, SpriteIndex = 0 };
            }
        }
        //todo: dirty check
        _tileDataBuffer.UpdateBuffer();

        _material.SetBuffer(ShaderResourceId.TileData, _tileDataBuffer);
        _material.SetBuffer(ShaderResourceId.SpriteData, _spriteDataBuffer);

        Transform = Transform3D.Identity;
    }

    public void Render(MaterialRenderer renderer)
    {
        renderer.DrawInstancedWithConstant(_mesh, _material, (uint)_tileDataBuffer.Length, new Constant { Model = Transform.Matrix });
    }

    public void SetTilesColor(ColorFloat color)
    {
        for (int i = 0; i < _tileDataBuffer.Length; i++)
        {
            TileData tileData = _tileDataBuffer[i];
            tileData.Color = color;
            _tileDataBuffer[i] = tileData;
        }
    }

    public void SetTilesColor(int2 from, int2 to, ColorFloat color)
    {
        for (int i = from.x; i < to.x; i++)
        {
            for (int j = from.y; j < to.y; j++)
            {
                SetTileColor(i, j, color);
            }
        }
    }

    public void SetTileColor(int x, int y, ColorFloat color)
    {
        TileData tileData = _tileDataBuffer[x * _tileDataBuffer.Length + y];
        tileData.Color = color;
        _tileDataBuffer[x * _tileDataBuffer.Length + y] = tileData;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tileDataBuffer.Dispose();
            _spriteDataBuffer.Dispose();
        }
    }
}
