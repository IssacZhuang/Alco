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
        public float BlendPriority;
    }

    private readonly List<Item> _items = new();

    public IReadOnlyList<Item> Items => _items;

    public Vector2 HeightOffsetFactor;
    public float BlendFactor;
    public float EdgeSmoothFactor;

    public void Add(Texture2D texture, TUserData userData)
    {
        _items.Add(new Item
        {
            Texture = texture,
            UserData = userData,
            MeshScale = Vector2.One,
            UVScale = Vector2.One,
            BlendPriority = 0.0f
        });
    }

    public void Add(
        Texture2D texture, 
        TUserData userData, 
        Vector2 meshScale, 
        Vector2 uvScale,
        float blendPriority
        )
    {
        _items.Add(new Item
        {
            Texture = texture,
            UserData = userData,
            MeshScale = meshScale,
            UVScale = uvScale,
            BlendPriority = blendPriority
        });
    }



    public void Clear()
    {
        _items.Clear();
    }
}

