using System;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS8625

namespace Vocore.Engine
{
    public class AssetManager
    {
        private const int FetchFinishJobAttempCount = 20;
        // key: filename, value: asset
        private readonly WeakCache<object> _weakCache = new WeakCache<object>();
        // key: filename, value: asset
        private readonly Dictionary<string, object> _strongCache = new Dictionary<string, object>();
        // key: extension, value: asset loader
        private readonly Dictionary<string, IBaseAssetLoader> _assetLoaders = new Dictionary<string, IBaseAssetLoader>();
        // key: filename, value: file source
        private readonly Dictionary<string, IFileSource> _fileEntries = new Dictionary<string, IFileSource>();
        private readonly PriorityList<IFileSource> _fileSources = new PriorityList<IFileSource>((a, b) => a.Order.CompareTo(b.Order));
        private readonly HashSet<string> _recongizedExtensions = new HashSet<string>();
        private bool _isEntryDirty = false;
        private bool _isRecongizedExtensionsDirty = false;
        private int _ownerThreadId;

        // Threads
        private struct AsyncPreprocessJob : IJob
        {
            public AssetCacheMode cacheMode;
            public object? preprocessed;
            public Func<object?> onPreprocess;
            public Func<object, object?> onCreate;
            public Action<object> onComplete;
            public void Execute()
            {
                preprocessed = onPreprocess();
            }
        }

        private readonly ThreadWorkerQueue<AsyncPreprocessJob> _asyncLoadQueue;

        public AssetManager(int threadCount)
        {
            _ownerThreadId = Environment.CurrentManagedThreadId;

            _asyncLoadQueue = new ThreadWorkerQueue<AsyncPreprocessJob>(threadCount);

            //built in asset loaders
            RegisterAssetLoader(new AssetLoaderTexture2D());
        }

        public IEnumerable<string> AllFileNames
        {
            get
            {
                TryRefreshEntries();
                return _fileEntries.Keys;
            }
        }

        public void RegisterAssetLoader<TAsset>(IAssetLoader<TAsset> assetLoader) where TAsset : class
        {
            foreach (var extension in assetLoader.FileExtensions)
            {
                if (_assetLoaders.TryGetValue(extension, out var loader))
                {
                    throw new Exception($"The asset loader for extension {extension} already exists: {loader.Name}");
                }
                _assetLoaders.Add(extension, assetLoader);
            }
            _isRecongizedExtensionsDirty = true;
            _isEntryDirty = true;
        }

        public void UnregisterAssetLoader<TAsset>(IAssetLoader<TAsset> assetLoader) where TAsset : class
        {
            foreach (var extension in assetLoader.FileExtensions)
            {
                if (_assetLoaders.TryGetValue(extension, out var loader))
                {
                    if (loader == assetLoader)
                    {
                        _assetLoaders.Remove(extension);
                    }
                }
            }
            _isRecongizedExtensionsDirty = true;
            _isEntryDirty = true;
        }

        public void AddFileSource(IFileSource fileSource)
        {
            _fileSources.Add(fileSource);
            _isEntryDirty = true;
        }

        public void RemoveFileSource(IFileSource fileSource)
        {
            _fileSources.Remove(fileSource);
            _isEntryDirty = true;
        }

        public bool TryRefreshEntries()
        {
            bool result = _isEntryDirty || _isRecongizedExtensionsDirty;
            UpdateRecongizedExtensions();
            UpdateEntries();
            return result;
        }

        public void ForceRefreshEntries()
        {
            UpdateRecongizedExtensions(true);
            UpdateEntries(true);
        }

        public bool TryLoadFromCache<TAsset>(string filename, [NotNullWhen(true)] out TAsset? asset) where TAsset : class
        {
            filename = ParseEntry(filename);
            return TryLoadFromCacheCore(filename, out asset);
        }

        public bool TryLoad<TAsset>(string filename, [NotNullWhen(true)] out TAsset? asset, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
        {
            CheckThread();
            TryRefreshEntries();
            filename = ParseEntry(filename);

            //try load from cache
            if (TryLoadFromCacheCore(filename, out asset))
            {
                return true;
            }

            // check the asset loader
            if (!TryGetLoader(filename, out IAssetLoader<TAsset>? assetLoaderT))
            {
                asset = null;
                return false;
            }

            // IO
            if (!_fileEntries.TryGetValue(filename, out IFileSource? fileSource))
            {
                Log.Error($"Trying to get asset {filename} but the file does not exist");
                asset = null;
                return false;
            }

            if (!fileSource.TryGetData(filename, out byte[]? data))
            {
                Log.Error($"Trying to get asset {filename} but the file does not exist");
                asset = null;
                return false;
            }

            // preprocess the asset
            if (!assetLoaderT.TryAsyncPreprocess(filename, data, out object? preprocessed))
            {
                Log.Error($"Trying to get asset {filename} but the asset loader failed to preprocess the asset");
                asset = null;
                return false;
            }

            // create the asset
            if (!assetLoaderT.TryCreateAsset(filename, preprocessed, out TAsset? newAsset))
            {
                Log.Error($"Trying to get asset {filename} but the asset loader failed to load the asset");
                asset = null;
                return false;
            }

            // try add to cache
            if (cacheMode == AssetCacheMode.Recyclable)
            {
                SetWeakCache(filename, newAsset);
            }
            else if (cacheMode == AssetCacheMode.Persistent)
            {
                SetStrongCache(filename, newAsset);
            }

            asset = newAsset;
            return true;
        }

        public void LoadAsync<TAsset>(string filename, Action<TAsset> action, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
        {
            CheckThread();
            TryRefreshEntries();
            filename = ParseEntry(filename);

            //try load from cache
            if (TryLoadFromCacheCore(filename, out TAsset? asset))
            {
                action(asset);
                return;
            }

            // check the asset loader
            if (!TryGetLoader(filename, out IAssetLoader<TAsset>? assetLoaderT))
            {
                // action(null);
                return;
            }

            AsyncPreprocessJob job = new AsyncPreprocessJob()
            {
                cacheMode = cacheMode,
                onPreprocess = GetAsyncPreprocessAction(filename, assetLoaderT),
                onCreate = GetOnCreateAction(filename, assetLoaderT),
                onComplete = GetOnCompleteAction(action)
            };

            _asyncLoadQueue.Push(job);
        }

        private Func<object?> GetAsyncPreprocessAction<TAsset>(string filename, IAssetLoader<TAsset> assetLoaderT) where TAsset : class
        {
            return () =>
            {
                // IO
                if (!_fileEntries.TryGetValue(filename, out IFileSource? fileSource))
                {
                    Log.Error($"Trying to get asset {filename} but the file does not exist");
                    return null;
                }

                if (!fileSource.TryGetData(filename, out byte[]? data))
                {
                    Log.Error($"Trying to get asset {filename} but the file does not exist");
                    return null;
                }

                if (!assetLoaderT.TryAsyncPreprocess(filename, data, out object? preprocessed))
                {
                    Log.Error($"Trying to get asset {filename} but the asset loader failed to preprocess the asset");
                    return null;
                }

                return preprocessed;
            };
        }

        private Func<object, object?> GetOnCreateAction<TAsset>(string filename, IAssetLoader<TAsset> assetLoaderT) where TAsset : class
        {
            return (object preprocessed) =>
            {
                if (!assetLoaderT.TryCreateAsset(filename, preprocessed, out TAsset? newAsset))
                {
                    Log.Error($"Trying to get asset {filename} but the asset loader failed to load the asset");
                    return null;
                }

                return newAsset;
            };
        }

        private Action<object> GetOnCompleteAction<TAsset>(Action<TAsset> action) where TAsset : class
        {
            return (object asset) =>
            {
                if (asset is TAsset newAsset)
                {
                    action(newAsset);
                }
                else
                {
                    Log.Error($"Can not cast asset to type {typeof(TAsset).Name}");
                }
            };
        }

        private bool TryLoadFromCacheCore<TAsset>(string filename, [NotNullWhen(true)] out TAsset? asset) where TAsset : class
        {
            if (_strongCache.TryGetValue(filename, out object? strongAsset))
            {
                if (strongAsset is TAsset strongAssetT)
                {
                    asset = strongAssetT;
                    return true;
                }
                else
                {
                    Log.Error($"Trying to get asset {filename} with type {typeof(TAsset).Name} but the asset is already loaded with type {strongAsset.GetType().Name}");
                    asset = null;
                    return false;
                }
            }
            if (_weakCache.TryGet(filename, out object? weakAsset))
            {
                if (weakAsset is TAsset weakAssetT)
                {
                    asset = weakAssetT;
                    return true;
                }
                else
                {
                    Log.Error($"Trying to get asset {filename} with type {typeof(TAsset).Name} but the asset is already loaded with type {weakAsset.GetType().Name}");
                    asset = null;
                    return false;
                }
            }

            asset = null;
            return false;
        }

        private bool TryGetLoader<TAsset>(string filename, [NotNullWhen(true)] out IAssetLoader<TAsset>? loader) where TAsset : class
        {
            string extension = Path.GetExtension(filename);
            if (!_assetLoaders.TryGetValue(extension, out IBaseAssetLoader? assetLoader))
            {
                Log.Error($"Trying to get asset {filename} but the asset loader does not exist");
                loader = null;
                return false;
            }

            if (assetLoader is not IAssetLoader<TAsset> assetLoaderT)
            {
                Log.Error($"Trying to get asset {filename} with type {typeof(TAsset).Name} but the asset loader does not support this type");
                loader = null;
                return false;
            }

            loader = assetLoaderT;
            return true;
        }

        public bool IsRecongizedExtension(string extension)
        {
            UpdateRecongizedExtensions();
            return _recongizedExtensions.Contains(extension);
        }

        public bool IsOwnerThread(int threadId)
        {
            return _ownerThreadId == threadId;
        }

        // Only called from the GameEngine class
        internal void OnUpdate()
        {
            for (int i = 0; i < FetchFinishJobAttempCount; i++)
            {
                StealingResult result = _asyncLoadQueue.TryGetFinishedTask(out AsyncPreprocessJob job);
                if (result == StealingResult.Success)
                {
                    if (job.preprocessed == null)
                    {
                        Log.Error("The asset manager failed to preprocess the asset");
                        continue;
                    }

                    job.onComplete(job.preprocessed);
                }

                if (result == StealingResult.Empty)
                {
                    return;
                }
            }

        }

        internal void SetMainThread()
        {
            _ownerThreadId = Environment.CurrentManagedThreadId;
        }

        private void CheckThread()
        {
            if (!IsOwnerThread(Environment.CurrentManagedThreadId))
            {
                throw new Exception("The asset manager can only be accessed by the thread that created it which usually is the main thread");
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

        private string ParseEntry(string entry)
        {
            return entry.Replace('/', '\\');
        }

        private void UpdateRecongizedExtensions(bool forced = false)
        {
            if (!_isRecongizedExtensionsDirty && !forced)
            {
                return;
            }

            _recongizedExtensions.Clear();
            foreach (var loader in _assetLoaders.Values)
            {
                foreach (var extension in loader.FileExtensions)
                {
                    _recongizedExtensions.Add(extension);
                }
            }
            _isRecongizedExtensionsDirty = false;
        }

        private void UpdateEntries(bool forced = false)
        {
            if (!_isEntryDirty && !forced)
            {
                return;
            }

            _fileEntries.Clear();
            foreach (var fileSource in _fileSources)
            {
                foreach (var file in fileSource.AllFileNames)
                {
                    string extension = Path.GetExtension(file);
                    if (_recongizedExtensions.Contains(extension))
                    {
                        _fileEntries.Add(file, fileSource);
                    }
                }
            }
            _isEntryDirty = false;
        }
    }
}