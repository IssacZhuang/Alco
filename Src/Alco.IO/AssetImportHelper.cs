namespace Alco.IO;

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

    private readonly ThreadWorkerQueue<AssetImportJob>? _workerQueue;
    // key: file extension, value: importer
    private readonly Dictionary<string, IAssetImporter> _importers;
    // only used when no thread is used
    private readonly List<AssetImportResult> _finishedJobs = new List<AssetImportResult>();
    private bool UseThread => _workerQueue != null;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="threadCount">The number of threads to use for importing assets. <br/><c>0</c> to disable threading.</param>
    public AssetImportHelper(int threadCount)
    {
        if (threadCount > 0)
        {
            _workerQueue = new ThreadWorkerQueue<AssetImportJob>(threadCount, "AssetImport");
        }

        _importers = new Dictionary<string, IAssetImporter>();
    }

    /// <summary>
    /// Register an asset importer, no duplicate importers for the same file extension is allowed.
    /// </summary>
    /// <param name="importer">The assets importer</param>
    /// <exception cref="InvalidOperationException"></exception>
    public void RegisterImporter(IAssetImporter importer)
    {
        foreach (var ext in importer.FileExtensions)
        {
            if (_importers.TryGetValue(ext, out var existingImporter))
            {
                throw new InvalidOperationException($"Importer for extension '{ext}' already exists: {existingImporter.Name}");
            }

            _importers[ext] = importer;
        }
    }

    /// <summary>
    /// Push a file to import worker thread.
    /// </summary>
    /// <param name="filename">The filename</param>
    /// <param name="file">The file content</param>
    /// <returns><c>true</c> if the file can be imported</returns> 
    public bool PushFile(string filename, byte[] file)
    {
        if (UseThread)
        {
            return PushFileThread(filename, file);
        }
        else
        {
            return PushFileNoThread(filename, file);
        }
    }

    private bool PushFileThread(string filename, byte[] file)
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
            _workerQueue!.Push(job);
            return true;
        }
        return false;
    }

    private bool PushFileNoThread(string filename, byte[] file)
    {
        string ext = Path.GetExtension(filename);
        if (_importers.TryGetValue(ext, out var importer))
        {
            try
            {
                AssetImportJob job = new AssetImportJob()
                {
                    importer = importer,
                    filename = filename,
                    file = file
                };
                job.Execute();
                _finishedJobs.Add(new AssetImportResult()
                {
                    Filename = job.filename,
                    ImportedFile = job.importedFile,
                    ImportedFilename = job.importedFilename,
                    exception = job.success ? null : new Exception("Failed to import file")
                });
                return true;
            }
            catch (Exception e)
            {
                _finishedJobs.Add(new AssetImportResult()
                {
                    Filename = filename,
                    ImportedFile = null,
                    ImportedFilename = null,
                    exception = e
                });
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Wait for all jobs to be completed.
    /// </summary>
    /// <returns> The results of all jobs.</returns>
    public IEnumerable<AssetImportResult> GetResults()
    {
        if (UseThread)
        {
            return GetResultsThread();
        }
        else
        {
            return GetResultsNoThread();
        }
    }

    private IEnumerable<AssetImportResult> GetResultsThread()
    {
        foreach (JobExcuteResult<AssetImportJob> result in _workerQueue!.WaitForAllCompleted())
        {
            yield return new AssetImportResult()
            {
                Filename = result.job.filename,
                ImportedFile = result.job.importedFile,
                ImportedFilename = result.job.importedFilename,
                exception = result.exception
            };
        }

        yield break;
    }

    private IEnumerable<AssetImportResult> GetResultsNoThread()
    {
        foreach (var result in _finishedJobs)
        {
            yield return result;
        }
        yield break;
    }

    public void Dispose()
    {
        _workerQueue?.Dispose();
    }
}