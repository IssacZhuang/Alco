using System.Numerics;

namespace Vocore.Rendering;

public class TileSetParams<TUserData>
{
    public struct Item
    {
        public TUserData UserData;
        public Vector2 MeshScale;
        public Vector2 UVScale;
        public Texture2D Texture;
    }

    private readonly List<Item> _items = new();

    public IReadOnlyList<Item> Items => _items;

    public void Add(Texture2D texture, TUserData userData)
    {
        _items.Add(new Item
        {
            Texture = texture,
            UserData = userData,
            MeshScale = Vector2.One,
            UVScale = Vector2.One
        });
    }

    public void Add(
        Texture2D texture, 
        TUserData userData, 
        Vector2 meshScale, 
        Vector2 uvScale,
        float blendFactor,
        float blendPriority
        )
    {
        _items.Add(new Item
        {
            Texture = texture,
            UserData = userData,
            MeshScale = meshScale,
            UVScale = uvScale
        });
    }



    public void Clear()
    {
        _items.Clear();
    }
}

