namespace Vocore.Engine;

/// <summary>
/// Importer for assets.
/// The import jobs in executed in multiple threads.
/// </summary>
public class AssetImportHelper : IDisposable
{
    private struct AssetImportJob : IJob
    {
        public IAssetImporter importer;
        public string filename;
        public byte[] file;
        public byte[]? importedFile;
        public string? importedFilename;
        public bool success;
        public void Execute()
        {
            success = importer.TryImport(filename, file, out importedFile, out importedFilename);
        }
    }

    private readonly ThreadWorkerQueue<AssetImportJob> _workerQueue;
    // key: file extension, value: importer
    private readonly Dictionary<string, IAssetImporter> _importers;

    public AssetImportHelper(int threadCount)
    {
        _workerQueue = new ThreadWorkerQueue<AssetImportJob>(threadCount, "AssetImport");
        _importers = new Dictionary<string, IAssetImporter>();
    }

    /// <summary>
    /// Register an asset importer, no duplicate importers for the same file extension is allowed.
    /// </summary>
    /// <param name="importer"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void RegisterImporter(IAssetImporter importer)
    {
        foreach (var ext in importer.FileExtensions)
        {
            if(_importers.TryGetValue(ext, out var existingImporter))
            {
                throw new InvalidOperationException($"Importer for extension '{ext}' already exists: {existingImporter.Name}");
            }

            _importers[ext] = importer;
        }
    }

    public void PushFile(string filename, byte[] file)
    {
        string ext = Path.GetExtension(filename);
        if (_importers.TryGetValue(ext, out var importer))
        {
            AssetImportJob job = new AssetImportJob()
            {
                importer = importer,
                filename = filename,
                file = file
            };
            _workerQueue.Push(job);
        }
    }



    public void Dispose()
    {
        _workerQueue.Dispose();
    }
}