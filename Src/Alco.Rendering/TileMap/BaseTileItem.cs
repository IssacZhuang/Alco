namespace Alco.Rendering;

public class BaseTileItem<TTileData, TUserData> where TTileData : unmanaged, ITileData
{
    public struct TextureData
    {
        public Texture2D Texture;
        public float Weight;
    }

    private readonly List<TextureData> _textures = new();

    public string Name { get; set; }
    public TTileData TileData { get; set; }
    public TUserData UserData { get; set; }

    public IReadOnlyList<TextureData> Textures => _textures;

    public BaseTileItem(string name, TTileData tileData, TUserData userData)
    {
        Name = name;
        TileData = tileData;
        UserData = userData;
    }

    public void AddTexture(Texture2D texture, float weight)
    {
        _textures.Add(new TextureData { Texture = texture, Weight = weight });
    }
}