
using Alco.Engine;
using Alco.Rendering;

/// <summary>Stores walls in a connectable tile grid and keeps the lighting manager's obstacle list in sync.</summary>
public class WallManager
{
    private readonly ConnectableTileBlock2D _wallGrid;
    private readonly LightingManager _lightingManager;


    /// <summary>Initializes the wall grid with the given dimensions.</summary>
    /// <param name="engine">Engine providing the rendering system.</param>
    /// <param name="lightingManager">Lighting manager that receives wall obstacles.</param>
    /// <param name="width">Grid width in tiles.</param>
    /// <param name="height">Grid height in tiles.</param>
    public WallManager(GameEngine engine, LightingManager lightingManager, int width, int height)
    {
        _wallGrid = new ConnectableTileBlock2D(engine.RenderingSystem, width, height, "wall_grid");
        _wallGrid.Transform.Position.Z = -1.5f;
        _lightingManager = lightingManager;
    }

    /// <summary>Adds a wall to the grid and registers it as a light obstacle.</summary>
    /// <param name="wall">Wall to add.</param>
    public void AddWall(Wall wall)
    {
        if (!_wallGrid.TrySetTileData(wall.Position.X, wall.Position.Y, wall))
        {
            return;
        }
        _lightingManager.AddObstacle(wall);
    }

    /// <summary>Removes a wall from the grid and unregisters it as a light obstacle.</summary>
    /// <param name="wall">Wall to remove.</param>
    public void RemoveWall(Wall wall)
    {
        _wallGrid.TryRemoveTileData(wall.Position.X, wall.Position.Y);
        _lightingManager.RemoveObstacle(wall);
    }

    /// <summary>Draws the wall grid into the given render pass.</summary>
    /// <param name="context">Active render pass scope.</param>
    public void Render(RenderPassScope context)
    {
        _wallGrid.OnRender(context);
    }
}