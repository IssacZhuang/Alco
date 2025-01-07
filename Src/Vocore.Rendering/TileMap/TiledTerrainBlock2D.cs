using System.Numerics;
using System.Runtime.InteropServices;

namespace Vocore.Rendering;

public class TiledTerrainBlock2D : AutoDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpriteData
    {
        public Rect UVRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TileData
    {
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tileDataBuffer.Dispose();
            _spriteDataBuffer.Dispose();
        }
    }
}
