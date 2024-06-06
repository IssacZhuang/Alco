namespace Vocore.IO;

public struct AssetImportResult
{
    public string Filename;
    public byte[]? ImportedFile;
    public string? ImportedFilename;
    public Exception? exception;
}