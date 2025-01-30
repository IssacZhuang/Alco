namespace Alco.Rendering;

public class BaseTileItem<TTileData, TUserData> where TTileData : unmanaged, ITileData
{
    private readonly List<Texture2D> _textures = new();

    public string Name { get; set; }
    public TTileData TileData { get; set; }
    public TUserData UserData { get; set; }

    public IReadOnlyList<Texture2D> Textures => _textures;

    public BaseTileItem(string name, TTileData tileData, TUserData userData)
    {
        Name = name;
        TileData = tileData;
        UserData = userData;
    }

    public BaseTileItem(string name, TTileData tileData, TUserData userData, params ReadOnlySpan<Texture2D> textures)
    {
        Name = name;
        TileData = tileData;
        UserData = userData;
        foreach (var texture in textures)
        {
            ArgumentNullException.ThrowIfNull(texture);
            _textures.Add(texture);
        }
    }

    public void AddTexture(Texture2D texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _textures.Add(texture);
    }
}