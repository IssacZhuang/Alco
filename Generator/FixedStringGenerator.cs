using System.IO;
using System.Text;

/// <summary>
/// Generator for creating FixedString classes with different maximum lengths.
/// </summary>
public class FixedStringGenerator : BaseGenerator
{
    /// <summary>
    /// Gets the output folder path for the generated FixedString files.
    /// </summary>
    public override string OutputFolder => "Src/Alco/String/~Generated";

    private readonly int[] _sizes = { 8, 16, 32, 64, 128, 256 };
    private readonly string _templatePath = "FixedString{n}.tt";

    /// <summary>
    /// Generates FixedString classes with different maximum capacities.
    /// </summary>
    public override void Generate()
    {
        ClearFolder();

        string templateContent = File.ReadAllText(Path.Combine(SolutionFolder, "Generator", _templatePath));

        foreach (var size in _sizes)
        {
            string content = templateContent.Replace("{0}", size.ToString());
            WriteFile($"FixedString{size}.gen.cs", content);
        }
    }
}