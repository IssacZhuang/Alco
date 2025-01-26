using System.Numerics;

namespace Alco.Rendering;

public class BaseTileSetParams<TTileData, TUserData> where TTileData : unmanaged, ITileData
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
    private readonly List<TTileData> _tileDatas = new();

    public int Count => _textures.Count;

    public void Add(Texture2D texture, TUserData userData, TTileData tileData)
    {
        ArgumentNullException.ThrowIfNull(texture);
        //ArgumentNullException.ThrowIfNull(userData);
        _textures.Add(texture);
        _userDatas.Add(userData);
        _tileDatas.Add(tileData);
    }

    public void Get(int index, out Texture2D texture, out TUserData userData, out TTileData tileData)
    {
        texture = _textures[index];
        userData = _userDatas[index];
        tileData = _tileDatas[index];
    }

    public void Clear()
    {
        _textures.Clear();
        _userDatas.Clear();
        _tileDatas.Clear();
    }
}
