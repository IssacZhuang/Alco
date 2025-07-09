
using System.Numerics;
using Alco;
using Alco.Rendering;



public interface IConnectableTile
{
    /// <summary>
    /// Gets the material of the tile.
    /// </summary>
    Material Material { get; }
    /// <summary>
    /// Gets the size of the tile.
    /// </summary>
    Vector2 Size { get; }
    /// <summary>
    /// Gets the offset of the tile.
    /// </summary>
    Vector2 Offset { get; }
    /// <summary>
    /// Gets the connect UV rect of the tile.
    /// </summary>
    /// <param name="connectDirection">The connect direction. The value is same to the enum <see cref="ConnectDirection"/></param>
    /// <returns>The connect UV rect.</returns>
    Rect GetConnectUVRect(int connectDirection);
}