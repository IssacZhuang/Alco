using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Vocore.Engine
{
    /// <summary>
    /// Represents an asset manager for managing runtime assets and file sources.
    /// </summary> 
    public class AssetManager
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
        private readonly Dictionary<string, string> _redirects = new Dictionary<string, string>();
        private readonly AssetWatcher _assetWatcher;
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

        private readonly ThreadWorkerQueue<AsyncPreprocessJob> _asyncLoadQueue;

        internal AssetManager(int threadCount)
        {
            _ownerThreadId = Environment.CurrentManagedThreadId;

            _asyncLoadQueue = new ThreadWorkerQueue<AsyncPreprocessJob>(threadCount);

            //built in asset loaders
            RegisterAssetLoader(new AssetLoaderTexture2D());
            RegisterAssetLoader(new AssetLoaderShaderHLSL((string includeName) =>
            {
                if (TryLoadDataFromSource(includeName, out ReadOnlySpan<byte> data))
                {
                    return Encoding.UTF8.GetString(data);
                }
                throw new Exception($"Can not find the include file: {includeName}");
            }));
            RegisterAssetLoader(new AssetLoaderShaderHLSLInclude());

            _assetWatcher = new AssetWatcher(OnAssetChanged);
        }

        /// <summary>
        /// Get all the file names of the assets
        /// </summary>
        public IEnumerable<string> AllFileNames
        {
            get
            {
                TryRefreshEntries();
                return _fileEntries.Keys;
            }
        }

        /// <summary>
        /// Register the asset loader to the asset manager
        /// </summary>
        /// <typeparam name="TAsset">The type of asset</typeparam>
        /// <param name="assetLoader">The asset loader to register</param>
        /// <exception cref="Exception">Thrown when the asset loader for the extension already exists</exception>
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

        /// <summary>
        /// Unregister the asset loader from the asset manager
        /// </summary>
        /// <typeparam name="TAsset">The type of asset</typeparam>
        /// <param name="assetLoader">The asset loader to unregister</param>
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

        /// <summary>
        /// Add the file source to the asset manager
        /// </summary>
        /// <param name="fileSource">The file source to add</param>
        public void AddFileSource(IFileSource fileSource)
        {
            _fileSources.Add(fileSource);
            _isEntryDirty = true;
        }

        /// <summary>
        /// Remove the file source from the asset manager
        /// </summary>
        /// <param name="fileSource">The file source to remove</param>
        public void RemoveFileSource(IFileSource fileSource)
        {
            if (_fileSources.Remove(fileSource))
            {
                fileSource.OnUnload();
            }
            _isEntryDirty = true;
        }

        /// <summary>
        /// Try to refresh the file entries and the recongized extensions if they are dirty
        /// </summary>
        /// <returns>True if the file entries or the recongized extensions are dirty</returns>
        public bool TryRefreshEntries()
        {
            bool result = _isEntryDirty || _isRecongizedExtensionsDirty;
            UpdateRecongizedExtensions();
            UpdateEntries();
            return result;
        }

        /// <summary>
        /// Force to refresh the file entries and the recongized extensions
        /// </summary>
        public void ForceRefreshEntries()
        {
            UpdateRecongizedExtensions(true);
            UpdateEntries(true);
        }

        /// <summary>
        /// Try to load the asset from the cache
        /// </summary>
        /// <typeparam name="TAsset">The type of asset</typeparam>
        /// <param name="filename">The filename of the asset</param>
        /// <param name="asset">The asset if it is loaded successfully; otherwise, <c>null</c>.</param>
        /// <returns>True if the asset is loaded successfully</returns>
        public bool TryLoadFromCache<TAsset>(string filename, [NotNullWhen(true)] out TAsset? asset) where TAsset : class
        {
            filename = ParseEntry(filename);
            return TryLoadFromCacheCore(filename, out asset);
        }

        /// <summary>
        /// Tries to load an asset of type <typeparamref name="TAsset"/> from the specified filename.
        /// </summary>
        /// <typeparam name="TAsset">The type of the asset to load.</typeparam>
        /// <param name="filename">The filename of the asset to load.</param>
        /// <param name="asset">When this method returns, contains the loaded asset if successful; otherwise, <c>null</c>.</param>
        /// <param name="cacheMode">The cache mode for the loaded asset. Default is <see cref="AssetCacheMode.Recyclable"/>.</param>
        /// <returns><c>true</c> if the asset was successfully loaded; otherwise, <c>false</c>.</returns>
        /// <exception cref="System.Exception">Thrown when an exception occurs during the loading process.</exception>
        public bool TryLoad<TAsset>(string filename, [NotNullWhen(true)] out TAsset? asset, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
        {
            CheckThread();
            TryRefreshEntries();
            filename = ParseEntry(filename);

            try
            {
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
                if (!TryLoadDataFromSource(filename, out ReadOnlySpan<byte> data))
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
                SetCache(filename, newAsset, cacheMode);
                asset = newAsset;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Exception on loading asset '{filename}': {ex}");
                asset = null;
                return false;
            }
        }

        /// <summary>
        /// Load asset file and preprocess the asset asynchronously, then call the onComplete action on the main thread.
        /// </summary>
        /// <typeparam name="TAsset">The type of asset.</typeparam>
        /// <param name="filename">Path and name of the asset file.</param>
        /// <param name="onComplete">The callback action when the asset is loaded.</param>
        /// <param name="cacheMode">Whether to cache the asset.</param>
        public void LoadAsync<TAsset>(string filename, Action<TAsset> onComplete, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
        {
            CheckThread();
            TryRefreshEntries();
            filename = ParseEntry(filename);

            //try load from cache
            if (TryLoadFromCacheCore(filename, out TAsset? asset))
            {
                onComplete(asset);
                return;
            }

            // check the asset loader
            if (!TryGetLoader(filename, out IAssetLoader<TAsset>? assetLoaderT))
            {
                Log.Error($"No asset loader found for the file '{filename}' to type {typeof(TAsset).Name}");
                // action(null);
                return;
            }

            AsyncPreprocessJob job = new AsyncPreprocessJob()
            {
                name = filename,
                onPreprocess = GetAsyncPreprocessAction(filename, assetLoaderT), // on worker thread
                onCreate = GetOnCreateAction(filename, assetLoaderT, cacheMode), // on main thread
                onComplete = GetOnCompleteAction(onComplete) // on main thread
            };

            _asyncLoadQueue.Push(job);
        }

        /// <summary>
        /// Try to load the raw data of the asset from the file source.
        /// </summary>
        /// <param name="filename">The filename of the asset.</param>
        /// <param name="data">The raw data of the asset if it is loaded successfully; otherwise, <c>null</c>.</param>
        /// <returns><c>True</c> if the asset is loaded successfully.</returns>
        public bool TryLoadRaw(string filename, [NotNullWhen(true)] out ReadOnlySpan<byte> data)
        {
            CheckThread();
            TryRefreshEntries();
            filename = ParseEntry(filename);

            if (TryLoadDataFromSource(filename, out data))
            {
                return true;
            }

            Log.Error($"Trying to get asset {filename} but the file does not exist");
            data = default;
            return false;
        }

        /// <summary>
        /// Set the redirect from one file to another file.
        /// </summary>
        /// <param name="from">The filename to redirect from.</param>
        /// <param name="to"> The filename to redirect to.</param>
        public void SetRedirect(string from, string to)
        {
            _redirects[from] = to;
        }

        /// <summary>
        /// Check if the file is redirected to another file and get the redirected file.
        /// </summary>
        /// <param name="from"> The filename to check.</param>
        /// <param name="to"> The redirected filename if it is redirected; otherwise, <c>null</c>.</param>
        /// <returns><c>True</c> if the file is redirected.</returns>
        public bool TryGetRedirect(string from, [NotNullWhen(true)] out string? to)
        {
            return _redirects.TryGetValue(from, out to);
        }

        public void DebugAddDirectorySourceAndWatch(DirectoryFileSource source)
        {
            AddFileSource(source);
            _assetWatcher.Watch(source.DirectoryPath);
        }

        private void OnAssetChanged(string assetPath)
        {

        }

        //on worker thread
        private Func<object?> GetAsyncPreprocessAction<TAsset>(string filename, IAssetLoader<TAsset> assetLoaderT) where TAsset : class
        {
            return () =>
            {
                if (!TryLoadDataFromSource(filename, out ReadOnlySpan<byte> data))
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

        //on main thread
        private Func<object, object?> GetOnCreateAction<TAsset>(string filename, IAssetLoader<TAsset> assetLoaderT, AssetCacheMode cacheMode) where TAsset : class
        {
            return (object preprocessed) =>
            {
                if (!assetLoaderT.TryCreateAsset(filename, preprocessed, out TAsset? newAsset))
                {
                    Log.Error($"Trying to get asset {filename} but the asset loader failed to load the asset");
                    return null;
                }

                SetCache(filename, newAsset, cacheMode);

                return newAsset;
            };
        }

        //on main thread
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
                    Log.Error($"Can not cast asset:{asset.GetType().Name} to type {typeof(TAsset).Name}");
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

        private bool TryLoadDataFromSource(string filename, out ReadOnlySpan<byte> data)
        {
            if (_fileEntries.TryGetValue(filename, out IFileSource? fileSource))
            {
                if (fileSource.TryGetData(filename, out data))
                {
                    return true;
                }
            }
            data = default;
            return false;
        }

        private bool TryGetLoader<TAsset>(string filename, [NotNullWhen(true)] out IAssetLoader<TAsset>? loader) where TAsset : class
        {
            string extension = Path.GetExtension(filename);
            if (!_assetLoaders.TryGetValue(extension, out IBaseAssetHandler? assetLoader))
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
                StealingResult result = _asyncLoadQueue.TryGetFinishedTask(out AsyncPreprocessJob job, out Exception? exception);
                if (result == StealingResult.Success)
                {
                    if (exception != null)
                    {
                        Log.Error($"Exception on loading asset '{job.name}': {exception}");
                        continue;
                    }

                    if (job.preprocessed == null)
                    {
                        Log.Error($"The preprocessed asset of '{job.name}' is null, the asset manager failed to load the asset");
                        continue;
                    }

                    try
                    {
                        object? asset = job.onCreate(job.preprocessed);
                        if (asset == null)
                        {
                            Log.Error($"Failed to create asset: {job.name}");
                            continue;
                        }
                        job.onComplete(asset);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Exception on creating asset '{job.name}': {e}");
                    }

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

        internal void Dispose()
        {
            _asyncLoadQueue.Dispose();
        }

        private void CheckThread()
        {
            if (!IsOwnerThread(Environment.CurrentManagedThreadId))
            {
                throw new Exception("The asset manager can only be accessed by the thread that created it which usually is the main thread");
            }
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