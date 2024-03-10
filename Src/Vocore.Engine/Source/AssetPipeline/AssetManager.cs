using System;

#pragma warning disable CS8625

namespace Vocore.Engine
{
    public class AssetManager
    {
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

        public AssetManager()
        {
            _ownerThreadId = Environment.CurrentManagedThreadId;
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

        public bool TryLoad<TAsset>(string filename, out TAsset asset, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
        {
            CheckThread();
            TryRefreshEntries();
            filename = ParseEntry(filename);
            if (_strongCache.TryGetValue(filename, out var strongAsset))
            {
                if (strongAsset is TAsset strongAssetT)
                {
                    asset = strongAssetT;
                    return true;
                }
                else
                {
                    Log.Warning($"Trying to get asset {filename} with type {typeof(TAsset).Name} but the asset is already loaded with type {strongAsset.GetType().Name}");
                    asset = null;
                    return false;
                }
            }
            if (_weakCache.TryGet(filename, out var weakAsset))
            {
                if (weakAsset is TAsset weakAssetT)
                {
                    asset = weakAssetT;
                    return true;
                }
                else
                {
                    Log.Warning($"Trying to get asset {filename} with type {typeof(TAsset).Name} but the asset is already loaded with type {weakAsset.GetType().Name}");
                    asset = null;
                    return false;
                }
            }


            if(!_fileEntries.TryGetValue(filename, out var fileSource))
            {
                Log.Warning($"Trying to get asset {filename} but the file does not exist");
                asset = null;
                return false;
            }

            if (!fileSource.TryGetData(filename, out var data))
            {
                Log.Warning($"Trying to get asset {filename} but the file does not exist");
                asset = null;
                return false;
            }

            var extension = Path.GetExtension(filename);
            if (!_assetLoaders.TryGetValue(extension, out var assetLoader))
            {
                Log.Warning($"Trying to get asset {filename} but the asset loader does not exist");
                asset = null;
                return false;
            }

            if (assetLoader is not IAssetLoader<TAsset> assetLoaderT)
            {
                Log.Warning($"Trying to get asset {filename} with type {typeof(TAsset).Name} but the asset loader does not support this type");
                asset = null;
                return false;
            }
            

            if (!assetLoaderT.OnLoad(filename, data, out var newAsset))
            {
                Log.Warning($"Trying to get asset {filename} but the asset loader failed to load the asset");
                asset = null;
                return false;
            }

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

        public bool IsRecongizedExtension(string extension)
        {
            UpdateRecongizedExtensions();
            return _recongizedExtensions.Contains(extension);
        }

        public bool IsOwnerThread(int threadId)
        {
            return _ownerThreadId == threadId;
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