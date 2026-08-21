using System.Text;

public class BuiltInAssetGenerator : BaseGenerator
{
    public override string OutputFolder => "Src/Alco.Engine/~Generated";

    private const string AssetsFolder = "Assets";
    private const string AssetPathFileName = "BuiltInAssetsPath.gen.cs";
    private const string AssetFileName = "BuiltInAssets.gen.cs";

    // Projects whose Assets folders contribute to the engine's built-in asset
    // constants. Every project the engine itself references (and thus whose
    // content flows into every application's output) can contribute; optional
    // on-demand modules (e.g. Alco.World3D) ship their own path constants
    // instead, so the engine never references assets it cannot load.
    private static readonly string[] AssetSourceProjects =
    [
        "Src/Alco.Engine",
        "Src/Alco.Rendering",
    ];

    public override void Generate()
    {
        ClearFolder();

        // Collect all asset files across the source projects, each paired with
        // its asset-root-relative path (the path the asset system loads by,
        // identical regardless of which project ships the file).
        var files = new List<(FileInfo File, string RelativePath)>();
        foreach (string projectFolder in AssetSourceProjects)
        {
            string assetsPath = Path.Combine(SolutionFolder, projectFolder, AssetsFolder);
            if (!Directory.Exists(assetsPath))
            {
                Console.WriteLine($"Assets folder not found at {assetsPath}");
                continue;
            }

            foreach (string filePath in Directory.GetFiles(assetsPath, "*.*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(assetsPath, filePath).Replace("\\", "/");
                files.Add((new FileInfo(filePath), relativePath));
            }
        }

        files.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));

        // Generate BuiltInAssetsPath.gen.cs
        var assetPathGenerator = new FileBuiltInAssetPath(files);
        string assetPathContent = assetPathGenerator.GenerateContent();
        WriteFile(AssetPathFileName, assetPathContent);

        // Generate BuiltInAssets.gen.cs
        var assetGenerator = new FileBuiltInAsset(files);
        string assetContent = assetGenerator.GenerateContent();
        WriteFile(AssetFileName, assetContent);
    }
}
