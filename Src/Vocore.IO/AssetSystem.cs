using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Vocore.IO
{
    /// <summary>
    /// Represents an asset manager for managing runtime assets and file sources.
    /// </summary> 
    public sealed partial  class AssetSystem
    {
        private const int FetchFinishJobAttempCount = 20;
        private readonly Dictionary<string, AssetHandle> _assetLookup = new Dictionary<string, AssetHandle>();
        // key: extension, value: asset loader
        private readonly Dictionary<string, IBaseAssetHandler> _assetLoaders = new Dictionary<string, IBaseAssetHandler>();
        // key: filename, value: file source
        private readonly Dictionary<string, IFileSource> _fileEntries = new Dictionary<string, IFileSource>();
        private readonly PriorityList<IFileSource> _fileSources = new PriorityList<IFileSource>((a, b) => a.Order.CompareTo(b.Order));
        private readonly HashSet<string> _recongizedExtensions = new HashSet<string>();
        private readonly ThreadWorkerQueue<AsyncPreprocessJob> _asyncLoadQueue;

        private readonly object _lock = new object();

        private bool _isEntryDirty = false;
        private bool _isRecongizedExtensionsDirty = false;
        private int _ownerThreadId;

        // Threads
        private struct AsyncPreprocessJob : IJob
        {
            public string name;
            public AssetHandle handle;
            public object? asset;
            public Func<object?> onCreate;
            
            public AssetCacheMode cacheMode;
            public void Execute()
            {
                asset = onCreate();
            }
        }

        internal AssetSystem(int threadCount)
        {
            if (threadCount <= 0)
            {
                throw new ArgumentException("Thread count must be greater than 0");
            }

            _ownerThreadId = Environment.CurrentManagedThreadId;
            _asyncLoadQueue = new ThreadWorkerQueue<AsyncPreprocessJob>(threadCount);
        }

        

        internal void Dispose()
        {
            _asyncLoadQueue.Dispose();
        }


    }
}