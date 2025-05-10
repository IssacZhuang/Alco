
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

    private readonly Mesh _mesh;

    private bool _isRenderDataDirty;

    public int Width => _width;
    public int Height => _height;

    public Transform3D Transform = Transform3D.Identity;

    public Action<Exception>? OnRenderError;

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

        _mesh = renderingSystem.MeshCenteredSprite;
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

    public void SetTileData(int x, int y, ConnectableTileData data)
    {
        _tileData.Set(x, y, data);
        UpdateConnectDirection(x, y);
        UpdateConnectDirection(x + 1, y);
        UpdateConnectDirection(x - 1, y);
        UpdateConnectDirection(x, y + 1);
        UpdateConnectDirection(x, y - 1);
        _isRenderDataDirty = true;
    }

    public void RemoveTileData(int x, int y)
    {
        if (_tileData.TryRemove(x, y, out _))
        {
            UpdateConnectDirection(x + 1, y);
            UpdateConnectDirection(x - 1, y);
            UpdateConnectDirection(x, y + 1);
            UpdateConnectDirection(x, y - 1);
            _isRenderDataDirty = true;
        }
    }

    private void BuildRenderCommand(GPURenderPass renderPass)
    {
        _subRenderContext.Begin(renderPass);
        Matrix4x4 matrix = Transform.Matrix;
        var tiles = _tileData.Infos;    
        for (int i = 0; i < tiles.Count; i++)
        {
            try
            {
                Grid2DCollection<ConnectableTileData>.Info info = tiles[i];
                int direction = _connectDirections[info.Y * _width + info.X];
                Rect uvRect = ConnectableTileData.GetConnectUVRect(direction);
                ConntectableTileConstant constant = new()
                {
                    Model = matrix,
                    Color = Vector4.One,
                    UvRect = uvRect,
                    Offset = new int2(info.X, info.Y)
                };
                _subRenderContext.DrawWithConstant(_mesh, info.Data.Material, constant);
            }
            catch (Exception e)
            {
                if (OnRenderError != null)
                {
                    OnRenderError(e);
                }
                else
                {
                    Log.Error(e);
                }
            }
        }

        _subRenderContext.End();
    }

    private void UpdateConnectDirection(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _width || y >= _height)
        {
            return;
        }
        _connectDirections[y * _width + x] = (int)GetConnectDirection(x, y);
    }

    private ConnectDirection GetConnectDirection(int x, int y)
    {
        ConnectDirection direction = ConnectDirection.None;
        if (_tileData.TryGet(x + 1, y, out _))
        {
            direction |= ConnectDirection.Right;
        }
        if (_tileData.TryGet(x - 1, y, out _))
        {
            direction |= ConnectDirection.Left;
        }
        if (_tileData.TryGet(x, y + 1, out _))
        {
            direction |= ConnectDirection.Up;
        }
        if (_tileData.TryGet(x, y - 1, out _))
        {
            direction |= ConnectDirection.Down;
        }
        return direction;
    }

    protected override void Dispose(bool disposing)
    {

    }
}

