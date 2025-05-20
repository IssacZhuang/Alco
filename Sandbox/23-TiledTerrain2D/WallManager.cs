
using Alco.Engine;
using Alco.Rendering;

public class WallManager
{
    private readonly ConnectableTileBlock2D _wallGrid;
    private readonly LightingManager _lightingManager;


    public WallManager(GameEngine engine, LightingManager lightingManager, int width, int height)
    {
        _wallGrid = engine.Rendering.CreateConnectableTileBlock2D(width, height, "wall_grid");
        _wallGrid.Transform.Position.Z = -1.5f;
        _lightingManager = lightingManager;
    }

    public void AddWall(Wall wall)
    {
        if (!_wallGrid.TrySetTileData(wall.Position.X, wall.Position.Y, wall.Data))
        {
            return;
        }
        _lightingManager.AddObstacle(wall);
    }

    public void RemoveWall(Wall wall)
    {
        _wallGrid.TryRemoveTileData(wall.Position.X, wall.Position.Y);
        _lightingManager.RemoveObstacle(wall);
    }

    public void Render(RenderContext context)
    {
        _wallGrid.OnRender(context);
    }
}