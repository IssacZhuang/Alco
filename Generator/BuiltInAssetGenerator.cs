using System.Text;

public class BuiltInAssetGenerator : BaseGenerator
{
    public override string OutputFolder => "Src/Vocore.Engine/~Generated";

    private const string AssetsFolder = "Assets";
    private const string VocoreEngineFolder = "Src/Vocore.Engine";
    private const string AssetPathFileName = "BuiltInAssetsPath.gen.cs";
    private const string AssetFileName = "BuiltInAssets.gen.cs";

    public override void Generate()
    {
        ClearFolder();

        // Get all files in Assets folder
        string assetsPath = Path.Combine(SolutionFolder, VocoreEngineFolder, AssetsFolder);
        if (!Directory.Exists(assetsPath))
        {
            Console.WriteLine($"Assets folder not found at {assetsPath}");
            return;
        }

        var files = Directory.GetFiles(assetsPath, "*.*", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .ToList();

        // Generate BuiltInAssetsPath.gen.cs
        var assetPathGenerator = new FileBuiltInAssetPath(files, assetsPath);
        string assetPathContent = assetPathGenerator.GenerateContent();
        WriteFile(AssetPathFileName, assetPathContent);

        // Generate BuiltInAssets.gen.cs
        var assetGenerator = new FileBuiltInAsset(files, assetsPath);
        string assetContent = assetGenerator.GenerateContent();
        WriteFile(AssetFileName, assetContent);
    }
}
