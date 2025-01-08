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
        public int2 Size;
    }

    //per sprite
    [StructLayout(LayoutKind.Sequential)]
    private struct SpriteData
    {
        public Rect UVRect;
        public Vector2 Scale;
    }

    //per tile
    [StructLayout(LayoutKind.Sequential)]
    private struct TileData
    {
        public ColorFloat Color;
        public uint SpriteIndex;
    }

    private uint _length;
    private int2 _size;
    private readonly TextureAtlas _textureAtlas;
    private readonly GraphicsArrayBuffer<ColorFloat> _colorData;
    private readonly GraphicsArrayBuffer<uint> _spriteIndexData;
    private readonly GraphicsArrayBuffer<SpriteData> _spriteData;
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
        _colorData = new GraphicsArrayBuffer<ColorFloat>(renderingSystem, width * height, "TiledTerrainBlock2D_ColorData");
        _spriteIndexData = new GraphicsArrayBuffer<uint>(renderingSystem, width * height, "TiledTerrainBlock2D_SpriteIndexData");
        _spriteData = new GraphicsArrayBuffer<SpriteData>(renderingSystem, textureAtlas.Sprites.Count, "TiledTerrainBlock2D_SpriteData");
        _material = material.CreateInstance();
        _mesh = renderingSystem.MeshSprite;

        for (int i = 0; i < textureAtlas.Sprites.Count; i++)
        {
            _spriteData[i] = new SpriteData { UVRect = textureAtlas.Sprites[i].UVRect };
        }

        for (int i = 0; i < _colorData.Length; i++)
        {
            _colorData[i] = ColorFloat.White;
        }

        for (int i = 0; i < _spriteIndexData.Length; i++)
        {
            _spriteIndexData[i] = 0;
        }

        _spriteData.UpdateBuffer();
        _colorData.UpdateBuffer();
        _spriteIndexData.UpdateBuffer();

        _material.SetRenderTexture(ShaderResourceId.Texture, _textureAtlas.RenderTexture);
        _material.SetBuffer(ShaderResourceId.ColorData, _colorData);
        _material.SetBuffer(ShaderResourceId.SpriteData, _spriteData);
        _material.SetBuffer(ShaderResourceId.SpriteIndexData, _spriteIndexData);

        Transform = Transform3D.Identity;
        _size = new int2(width, height);
        _length = (uint)(width * height);
    }

    public void Render(MaterialRenderer renderer)
    {
        renderer.DrawInstancedWithConstant(_mesh, _material, _length, new Constant { Model = Transform.Matrix, Size = _size });
    }

    public void SetTilesColor(ColorFloat color)
    {
        for (int i = 0; i < _length; i++)
        {
            _colorData[i] = color;
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
        _colorData[y * _size.x + x] = color;
    }

    public void SetTileSpriteIndex(int x, int y, uint spriteIndex)
    {
        _spriteIndexData[y * _size.x + x] = spriteIndex;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _colorData.Dispose();
            _spriteData.Dispose();
            _spriteIndexData.Dispose();
        }
    }
}
