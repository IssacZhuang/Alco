using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Alco.Graphics;

using static Alco.math;

namespace Alco.Rendering;

/// <summary>
/// Represents a 2D tile block where each tile can connect to its neighbors.
/// </summary>
public class ConnectableTileBlock2D : AutoDisposable
{
    /// <summary>
    /// Represents a 2D tile block where each tile can connect to its neighbors.
    /// </summary>
    private readonly Grid2DCollection<IConnectableTile> _tileData;
    private readonly int[] _connectDirections;
    private readonly SubRenderContext _subRenderContext;

    protected readonly int2 _size;

    private readonly Mesh _mesh;

    private bool _isRenderDataDirty;

    /// <summary>
    /// Gets the size of the tile block in tiles.
    /// </summary>
    public int2 Size => _size;

    /// <summary>
    /// Gets or sets the transform of the tile block.
    /// </summary>
    public Transform3D Transform = Transform3D.Identity;

    /// <summary>
    /// Gets or sets the callback for handling rendering errors.
    /// </summary>
    public Action<Exception>? OnRenderError;

    /// <summary>
    /// Gets the name of the tile block.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectableTileBlock2D"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system to use for this tile block.</param>
    /// <param name="width">The width of the tile block in tiles.</param>
    /// <param name="height">The height of the tile block in tiles.</param>
    /// <param name="name">The name of the tile block.</param>
    internal ConnectableTileBlock2D(
        RenderingSystem renderingSystem,
        int width,
        int height,
        string name)
    {
        _size = new int2(width, height);
        _tileData = new Grid2DCollection<IConnectableTile>(width, height);
        _connectDirections = new int[width * height];
        _subRenderContext = renderingSystem.CreateSubRenderContext("connectable_tile_block_2d");
        _isRenderDataDirty = true;

        _mesh = renderingSystem.MeshCenteredSprite;
        Name = name;
    }

    /// <summary>
    /// Renders the tile block.
    /// </summary>
    /// <param name="renderer">The render context to use for rendering.</param>
    public void OnRender(RenderContext renderer)
    {
        if (_isRenderDataDirty)
        {
            BuildRenderCommand(renderer.Framebuffer!.AttachmentLayout);
            _isRenderDataDirty = false;
        }

        renderer.ExecuteSubContext(_subRenderContext);
    }

    /// <summary>
    /// Attempts to get the tile data at the specified coordinates (pixel space).
    /// </summary>
    /// <remarks>
    /// Pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </remarks>
    /// <param name="x">X coordinate of the tile in pixel space</param>
    /// <param name="y">Y coordinate of the tile in pixel space</param>
    /// <param name="data">When this method returns, contains the tile data if the tile exists; otherwise, null.</param>
    /// <returns>True if the tile data exists at the specified coordinates; otherwise, false.</returns>
    public bool TryGetTileData(int x, int y, [NotNullWhen(true)] out IConnectableTile? data)
    {
        return _tileData.TryGet(x, y, out data);
    }

    /// <summary>
    /// Attempts to get the tile data at the specified coordinates (pixel space).
    /// </summary>
    /// <remarks>
    /// Pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </remarks>
    /// <param name="tilePosition">The tile coordinates in pixel space</param>
    /// <param name="data">When this method returns, contains the tile data if the tile exists; otherwise, null.</param>
    /// <returns>True if the tile data exists at the specified coordinates; otherwise, false.</returns>
    public bool TryGetTileData(int2 tilePosition, [NotNullWhen(true)] out IConnectableTile? data)
    {
        return _tileData.TryGet(tilePosition.X, tilePosition.Y, out data);
    }

    /// <summary>
    /// Sets tile data at the specified coordinates (pixel space).
    /// </summary>
    /// <remarks>
    /// Pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </remarks>
    /// <param name="x">X coordinate of the tile in pixel space</param>
    /// <param name="y">Y coordinate of the tile in pixel space</param>
    /// <param name="data">Tile data to set</param>
    /// <returns>True if the tile data was set, false if it was already set to the same value</returns>
    public bool TrySetTileData(int x, int y, IConnectableTile data)
    {
        if (_tileData.TryGet(x, y, out var oldData) && oldData == data)
        {
            return false;
        }
        _tileData.AddOrUpdate(x, y, data);
        UpdateConnectDirection(x, y);
        UpdateConnectDirection(x + 1, y);
        UpdateConnectDirection(x - 1, y);
        UpdateConnectDirection(x, y + 1);
        UpdateConnectDirection(x, y - 1);
        _isRenderDataDirty = true;
        return true;
    }

    /// <summary>
    /// Sets tile data at the specified coordinates (pixel space).
    /// </summary>
    /// <remarks>
    /// Pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </remarks>
    /// <param name="tilePosition">The tile coordinates in pixel space</param>
    /// <param name="data">Tile data to set</param>
    /// <returns>True if the tile data was set, false if it was already set to the same value</returns>
    public bool TrySetTileData(int2 tilePosition, IConnectableTile data)
    {
        return TrySetTileData(tilePosition.X, tilePosition.Y, data);
    }

    /// <summary>
    /// Removes tile data at the specified coordinates (pixel space).
    /// </summary>
    /// <remarks>
    /// Pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </remarks>
    /// <param name="x">X coordinate of the tile in pixel space</param>
    /// <param name="y">Y coordinate of the tile in pixel space</param>
    /// <returns>True if the tile data was removed, false if there was no tile data at the specified coordinates</returns>
    public bool TryRemoveTileData(int x, int y)
    {
        if (_tileData.TryRemove(x, y, out _))
        {
            UpdateConnectDirection(x + 1, y);
            UpdateConnectDirection(x - 1, y);
            UpdateConnectDirection(x, y + 1);
            UpdateConnectDirection(x, y - 1);
            _isRenderDataDirty = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes tile data at the specified coordinates (pixel space).
    /// </summary>
    /// <remarks>
    /// Pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </remarks>
    /// <param name="tilePosition">The tile coordinates in pixel space</param>
    /// <returns>True if the tile data was removed, false if there was no tile data at the specified coordinates</returns>
    public bool TryRemoveTileData(int2 tilePosition)
    {
        return TryRemoveTileData(tilePosition.X, tilePosition.Y);
    }

    /// <summary>
    /// Sets the render data dirty.
    /// </summary>
    public void SetRenderDataDirty()
    {
        _isRenderDataDirty = true;
    }

    private void BuildRenderCommand(GPUAttachmentLayout renderPass)
    {
        _subRenderContext.Begin(renderPass);
       Matrix4x4 matrix = Transform.Matrix;
        var tiles = _tileData.Infos;

        float halfWidth = (_size.X - 1) * 0.5f;
        float halfHeight = (_size.Y - 1) * 0.5f;
        for (int i = 0; i < tiles.Count; i++)
        {
            try
            {
                Grid2DCollection<IConnectableTile>.Info info = tiles[i];
                int direction = _connectDirections[info.Y * _size.X + info.X];
                IConnectableTile tile = info.Data;
                Rect uvRect = tile.GetConnectUVRect(direction);
                ConntectableTileConstant constant = new()
                {
                    Model = matrix,
                    Color = Vector4.One,
                    UvRect = uvRect,
                    Size = tile.Size,
                    Offset = new Vector2(info.X - halfWidth, -info.Y + halfHeight) + tile.Offset
                };
                _subRenderContext.DrawWithConstant(_mesh, tile.Material, constant);
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
        if (x < 0 || y < 0 || x >= _size.X || y >= _size.Y)
        {
            return;
        }
        _connectDirections[y * _size.X + x] = (int)GetConnectDirection(x, y);
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
        if (_tileData.TryGet(x, y - 1, out _))
        {
            direction |= ConnectDirection.Up;
        }
        if (_tileData.TryGet(x, y + 1, out _))
        {
            direction |= ConnectDirection.Down;
        }
        return direction;
    }

    protected override void Dispose(bool disposing)
    {

    }
}

