using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

/// <summary>
/// Importer for assets, it will import and convert the asset to another type<br/>
/// For example, a hlsl shader will be imported and compiled to binary.
/// </summary>
public interface IAssetImporter : IBaseAssetHandler
{
    /// <summary>
    /// Tries to import a file.
    /// </summary>
    /// <param name="filename">The name of the file to import.</param>
    /// <param name="file">The content of the file to import.</param>
    /// <param name="importedFile">The data of the imported file</param>
    /// <param name="importedFilename">The filename of the imported file</param>
    /// <returns><c>true</c> if the import was successful</returns>
    bool TryImport(
        string filename,
        byte[] file,
        [NotNullWhen(true)] out byte[] importedFile,
        [NotNullWhen(true)] out string? importedFilename);
}