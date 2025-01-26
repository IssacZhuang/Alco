using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Alco.IO
{
    /// <summary>
    /// Represents an asset manager for managing runtime assets and file sources.
    /// </summary> 
    public sealed partial  class AssetSystem
    {
        private readonly ThreadLocal<AssetProfiler> Profiler = new ThreadLocal<AssetProfiler>(() => new AssetProfiler());

        private const int FetchJobAttempCount = 20;
        private readonly ConcurrentDictionary<string, AssetHandle> _assetLookup = new ConcurrentDictionary<string, AssetHandle>();
        // key: extension, value: asset loader
        private readonly Dictionary<string, IBaseAssetHandler> _assetLoaders = new Dictionary<string, IBaseAssetHandler>();
        // key: filename, value: file source
        private readonly Dictionary<string, IFileSource> _fileEntries = new Dictionary<string, IFileSource>();
        private readonly PriorityList<IFileSource> _fileSources = new PriorityList<IFileSource>((a, b) => a.Priority.CompareTo(b.Priority));
        private readonly HashSet<string> _recongizedExtensions = new HashSet<string>();

        private readonly ThreadWorkerQueue<AsyncPreprocessJob> _asyncLoadQueue;
        private AtomicSpinLock _lockJobQueue = new AtomicSpinLock();
        private readonly object _lockEntry = new object();
        private readonly object _lockExtensions = new object();

        private volatile bool _isEntryDirty = false;
        private volatile bool _isRecongizedExtensionsDirty = false;

        private readonly IAssetSystemHost _host;

        // Threads
        private struct AsyncPreprocessJob : IJob
        {
            public string name;
            public AssetHandle handle;
            public Func<object?> onCreate;
            public AssetCacheMode cacheMode;
            public void Execute()
            {
                handle.tmpAsset = onCreate();
            }
        }

        public bool IsProfileEnabled { get; set; } = false;

        public AssetSystem(IAssetSystemHost host, int threadCount, bool isProfileEnabled = false)
        {
            if (threadCount <= 0)
            {
                throw new ArgumentException("Thread count must be greater than 0");
            }

            IsProfileEnabled = isProfileEnabled;
            _asyncLoadQueue = new ThreadWorkerQueue<AsyncPreprocessJob>(threadCount);
            host.OnHandleAssetLoaded += OnHandleAssetLoaded;
            host.OnDispose += Dispose;

            _host = host;

            _host.LogSuccess("Asset system created");
        }



        private void Dispose()
        {
            RemoveAllFileSource();
            _asyncLoadQueue.Dispose();
            Profiler.Dispose();

            _host.LogInfo("Asset system closed");
            _host.OnHandleAssetLoaded -= OnHandleAssetLoaded;
            _host.OnDispose -= Dispose;
        }


    }
}