using System.Numerics;

namespace Vocore.Rendering;

public class SurfaceTileSetParams<TUserData>
{
    public struct Item
    {
        public TUserData UserData;
        public Texture2D Texture;
        public Vector2 MeshScale;
        public Vector2 UVScale;
        
        public float BlendPriority;
    }

    private readonly List<Texture2D> _textures = new();
    private readonly List<TUserData> _userDatas = new();
    private readonly List<SurfaceTileData> _surfaceTileDatas = new();

    public int Count => _textures.Count;

    public void Add(Texture2D texture, TUserData userData, SurfaceTileData tileData)
    {
        ArgumentNullException.ThrowIfNull(texture);
        //ArgumentNullException.ThrowIfNull(userData);
        _textures.Add(texture);
        _userDatas.Add(userData);
        _surfaceTileDatas.Add(tileData);
    }

    public void Get(int index, out Texture2D texture, out TUserData userData, out SurfaceTileData tileData)
    {
        texture = _textures[index];
        userData = _userDatas[index];
        tileData = _surfaceTileDatas[index];
    }

    public void Clear()
    {
        _textures.Clear();
        _userDatas.Clear();
        _surfaceTileDatas.Clear();
    }
}

