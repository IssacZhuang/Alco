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
        // key: filename, value: asset
        private readonly ConcurrentWeakCache<object> _weakCache = new ConcurrentWeakCache<object>();
        // key: filename, value: asset
        private readonly ConcurrentDictionary<string, object> _strongCache = new ConcurrentDictionary<string, object>();
        // key: extension, value: asset loader
        private readonly Dictionary<string, IBaseAssetHandler> _assetLoaders = new Dictionary<string, IBaseAssetHandler>();
        // key: filename, value: file source
        private readonly Dictionary<string, IFileSource> _fileEntries = new Dictionary<string, IFileSource>();
        private readonly PriorityList<IFileSource> _fileSources = new PriorityList<IFileSource>((a, b) => a.Order.CompareTo(b.Order));
        private readonly HashSet<string> _recongizedExtensions = new HashSet<string>();
        private readonly ThreadWorkerQueue<AsyncPreprocessJob> _asyncLoadQueue;

        private bool _isEntryDirty = false;
        private bool _isRecongizedExtensionsDirty = false;
        private int _ownerThreadId;

        // Threads
        private struct AsyncPreprocessJob : IJob
        {
            public string name;
            public object? preprocessed;
            public Func<object?> onPreprocess;
            public Func<object, object?> onCreate;
            public Action<object> onComplete;
            public void Execute()
            {
                preprocessed = onPreprocess();
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

       

        private void SetCache(string filename, object asset, AssetCacheMode cacheMode)
        {
            if (cacheMode == AssetCacheMode.Recyclable)
            {
                SetWeakCache(filename, asset);
            }
            else if (cacheMode == AssetCacheMode.Persistent)
            {
                SetStrongCache(filename, asset);
            }
        }

        private void SetWeakCache(string filename, object asset)
        {
            _weakCache.Set(filename, asset);
        }

        private void SetStrongCache(string filename, object asset)
        {
            _strongCache[filename] = asset;
        }
    }
}