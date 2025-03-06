namespace Alco.IO;

public readonly ref struct AssetLoadContext
{
    public readonly AssetSystem AssetSystem;
    public readonly string Filename;
    public readonly ReadOnlySpan<byte> Data;
    public readonly Type AssetType;

    public AssetLoadContext(AssetSystem assetSystem, string filename, ReadOnlySpan<byte> data, Type assetType)
    {
        AssetSystem = assetSystem;
        Filename = filename;
        Data = data;
        AssetType = assetType;
    }
}
