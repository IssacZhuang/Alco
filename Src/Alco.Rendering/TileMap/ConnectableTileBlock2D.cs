
using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

public class ConnectableTileBlock2D : AutoDisposable
{
    private readonly Grid2DCollection<ConnectableTileData> _tileData;
    private readonly int[] _connectDirections;
    private readonly SubRenderContext _subRenderContext;

    private readonly int _width;
    private readonly int _height;

    private bool _isRenderDataDirty;

    public int Width => _width;
    public int Height => _height;

    public Transform3D Transform = Transform3D.Identity;

    internal ConnectableTileBlock2D(
        RenderingSystem renderingSystem,
        int width,
        int height)
    {
        _width = width;
        _height = height;
        _tileData = new Grid2DCollection<ConnectableTileData>(width, height);
        _connectDirections = new int[width * height];
        _subRenderContext = renderingSystem.CreateSubRenderContext("connectable_tile_block_2d");
        _isRenderDataDirty = true;
    }

    public void OnRender(RenderContext renderer)
    {
        if (_isRenderDataDirty)
        {
            BuildRenderCommand(renderer.Framebuffer!.RenderPass);
            _isRenderDataDirty = false;
        }

        renderer.ExecuteSubContext(_subRenderContext);
    }

    private void BuildRenderCommand(GPURenderPass renderPass)
    {
        _subRenderContext.Begin(renderPass);
        Matrix4x4 matrix = Transform.Matrix;
        var tiles = _tileData.Infos;    
        for (int i = 0; i < tiles.Count; i++)
        {
            var info = tiles[i];
            var direction = _connectDirections[info.Y * _width + info.X];
            Sprite sprite = info.Data.GetConnectedSprite(direction);
        }

        _subRenderContext.End();
    }

    protected override void Dispose(bool disposing)
    {

    }
}

