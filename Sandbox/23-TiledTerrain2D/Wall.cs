using Alco;
using Alco.Graphics;
using Alco.Rendering;

public class Wall : IObstacle
{
    public int2 Position { get; set; }
    public Color32 Opacity => Data.LightMapOpacity;
    public ConnectableTileData Data { get; }

    public Wall(int2 position, ConnectableTileData data)
    {
        Position = position;
        Data = data;
    }
}


