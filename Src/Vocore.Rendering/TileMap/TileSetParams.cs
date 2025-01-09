using System.Numerics;

namespace Vocore.Rendering;

public class TileSetParams<TUserData>
{
    public struct Item
    {
        public TUserData UserData;
        public Vector2 Scale;
        public Texture2D Texture;
    }

    private readonly List<Item> _items = new();

    public IReadOnlyList<Item> Items => _items;

    public void Add(Texture2D texture, TUserData userData)
    {
        _items.Add(new Item { Texture = texture, UserData = userData });
    }

    public void Clear()
    {
        _items.Clear();
    }
}

