using System.Numerics;
using Alco;
using Alco.Graphics;
using Alco.Rendering;

/// <summary>A tile wall that blocks light and selects its sprite from the 16 connection variants.</summary>
public class Wall : IObstacle, IConnectableTile
{
    private static readonly Rect[] _connectedSprites = new Rect[16];

    static Wall()
    {
        for (int i = 0; i < 16; i++)
        {
            _connectedSprites[i] = UVUtility.CalculateFrameUVRect(4, 4, i);
        }
    }

    /// <summary>Grid position of the wall.</summary>
    public int2 Position { get; set; }
    /// <summary>Opacity applied to the wall when light passes through it.</summary>
    public Color32 Opacity { get; set; }

    /// <summary>Material used to render the wall.</summary>
    public GraphicsMaterial Material {get;}

    /// <summary>Size of the wall in world units.</summary>
    public Vector2 Size {get;}

    /// <summary>Render offset applied to the wall's sprite.</summary>
    public Vector2 Offset {get;}

    /// <summary>Initializes a wall with its position, material, size, offset, and opacity.</summary>
    /// <param name="position">Grid position of the wall.</param>
    /// <param name="material">Material used to render the wall.</param>
    /// <param name="size">Size of the wall in world units.</param>
    /// <param name="offset">Render offset applied to the wall's sprite.</param>
    /// <param name="opacity">Opacity applied when light passes through the wall.</param>
    public Wall(int2 position, GraphicsMaterial material, Vector2 size, Vector2 offset, Color32 opacity)
    {
        Position = position;
        Material = material;
        Size = size;
        Offset = offset;
        Opacity = opacity;
    }

    /// <summary>Gets the sprite UV rect for the given connection mask.</summary>
    /// <param name="connectDirection">Bitmask of connected neighboring tiles.</param>
    /// <returns>The UV rect of the matching connection variant.</returns>
    public Rect GetConnectUVRect(int connectDirection)
    {
        return _connectedSprites[connectDirection];
    }
}


