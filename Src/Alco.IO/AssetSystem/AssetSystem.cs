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
        private readonly Dictionary<string, IAssetLoader> _assetLoaders = new Dictionary<string, IAssetLoader>();
        // key: alias, value: filename
        private readonly ConcurrentDictionary<string, string> _assetAliases = new ConcurrentDictionary<string, string>();
        // key: filename, value: file source
        private readonly Dictionary<string, IFileSource> _fileEntries = new Dictionary<string, IFileSource>();
        private readonly PriorityList<IFileSource> _fileSources = new PriorityList<IFileSource>((a, b) => a.Priority.CompareTo(b.Priority));
        private readonly HashSet<string> _recongizedExtensions = new HashSet<string>();

        private readonly object _lockEntry = new object();
        private readonly object _lockExtensions = new object();

        private volatile bool _isEntryDirty = false;
        private volatile bool _isRecongizedExtensionsDirty = false;

        private readonly IAssetSystemHost _host;

        public bool IsProfileEnabled { get; set; } = false;

        public AssetSystem(IAssetSystemHost host, bool isProfileEnabled = false)
        {
            IsProfileEnabled = isProfileEnabled;
            host.OnDispose += Dispose;

            _host = host;

            _host.LogSuccess("Asset system created");
        }



        private void Dispose()
        {
            RemoveAllFileSource();
            Profiler.Dispose();

            _host.LogInfo("Asset system closed");
            _host.OnDispose -= Dispose;

            _httpClient?.Dispose();
        }


    }
}