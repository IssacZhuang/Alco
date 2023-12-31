using System;

#pragma warning disable CS8625

namespace Vocore.Engine
{
    public class AssetManager
    {
        private readonly WeakCache _weakCache = new WeakCache();
        private readonly Dictionary<string, object> _strongCache = new Dictionary<string, object>();
        private readonly Dictionary<string, IBaseAssetLoader> _assetLoaders = new Dictionary<string, IBaseAssetLoader>();
        private readonly Dictionary<string, IFileSource> _fileEntries = new Dictionary<string, IFileSource>();
        private readonly List<IFileSource> _fileSources = new List<IFileSource>();
        private bool _isEntryDirty = false;

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

        public void UpdateEntries()
        {
            if (!_isEntryDirty)
            {
                return;
            }

            _fileEntries.Clear();
            foreach (var fileSource in _fileSources)
            {
                foreach (var file in fileSource.AllFileNames)
                {
                    _fileEntries.Add(file, fileSource);
                }
            }
            _isEntryDirty = false;
        }

        public bool TryGet<TAsset>(string filename, out TAsset asset, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
        {
            UpdateEntries();
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
            

            if (!assetLoaderT.TryLoad(filename, data, out var newAsset))
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