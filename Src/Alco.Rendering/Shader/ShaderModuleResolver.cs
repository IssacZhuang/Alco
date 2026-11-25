using Alco.Graphics;

namespace Alco.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// Module-name → asset-path resolution: slang probes a module name
// ('AlcoRendering_Core') as several file forms ('AlcoRendering/Core.slang',
// 'AlcoRendering-Core.slang', 'AlcoRendering_Core.slang'). The engine resolver
// answers those probes against the asset system by comparing dashed forms, so a
// module's file can live anywhere under Assets/ (Libs/, Passes/…) without
// its directory position being load-bearing.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Builds slang module resolvers over a name-addressed asset source.</summary>
public static class ShaderModuleResolver
{
    /// <summary>Creates a resolver that matches exact asset paths and complete module file names.</summary>
    /// <param name="openStream">Opens an asset by its exact asset-system path.</param>
    /// <param name="listNames">Lists all asset names (used for probe matching).</param>
    /// <returns>The resolver used by the slang compiler.</returns>
    public static ShaderFileResolver Create(
        Func<string, Stream?> openStream,
        Func<IEnumerable<string>> listNames)
    {
        return path =>
        {
            string key = NormalizeKey(path);

            // 1) exact asset path
            Stream? exact = openStream(key);
            if (exact != null)
            {
                using (exact)
                {
                    return ReadAll(exact);
                }
            }

            // 2) dashed probe matching: 'Libs/AlcoRendering_Core.slang' answers
            //    'AlcoRendering/Core.slang', 'AlcoRendering-Core.slang', …
            //    Relative import probes ('Shaders/Materials/Surface.slang' for
            //    'import Surface;' from a module in that folder) retry on the probe's
            //    base name: module names are global, so a module's directory
            //    position is never load-bearing.
            string? match = ProbeDashed(key, listNames, openStream);
            if (match != null)
            {
                return match;
            }
            string baseName = Path.GetFileName(key);
            if (baseName != key)
            {
                match = ProbeDashed(baseName, listNames, openStream);
                if (match != null)
                {
                    return match;
                }
            }
            return null;
        };
    }

    /// <summary>
    /// Creates an indexed, version-aware resolver pair for O(1) module lookup and existence probing
    /// without enumerating assets eagerly at creation time.
    /// </summary>
    /// <param name="openStream">Opens an asset by its exact asset-system path.</param>
    /// <param name="listNames">Lists all asset names whenever the asset version changes.</param>
    /// <param name="getVersion">Returns a monotonic version counter incremented when asset entries change.</param>
    /// <returns>The indexed file resolver and path-existence delegates.</returns>
    public static (ShaderFileResolver FileResolver, ShaderPathExists PathExists) CreateIndexed(
        Func<string, Stream?> openStream,
        Func<IEnumerable<string>> listNames,
        Func<int> getVersion)
    {
        ArgumentNullException.ThrowIfNull(openStream);
        ArgumentNullException.ThrowIfNull(listNames);
        ArgumentNullException.ThrowIfNull(getVersion);

        Lock buildLock = new();
        IndexSnapshot? index = null;

        IndexSnapshot GetSnapshot()
        {
            int version = getVersion();
            IndexSnapshot? current = Volatile.Read(ref index);
            if (current != null && current.Version == version)
                return current;

            lock (buildLock)
            {
                version = getVersion();
                current = index;
                if (current != null && current.Version == version)
                    return current;

                IndexSnapshot rebuilt = BuildSnapshot(listNames(), version);
                Volatile.Write(ref index, rebuilt);
                return rebuilt;
            }
        }

        string? Resolve(string path)
        {
            string key = NormalizeKey(path);
            IndexSnapshot snapshot = GetSnapshot();
            string? assetPath = FindIndexedAssetPath(snapshot, key);
            if (assetPath != null)
            {
                Stream? stream = openStream(assetPath);
                if (stream != null)
                {
                    using (stream)
                        return ReadAll(stream);
                }
            }

            Stream? fallback = openStream(key);
            if (fallback != null)
            {
                using (fallback)
                    return ReadAll(fallback);
            }

            return null;
        }

        bool Exists(string path)
        {
            string key = NormalizeKey(path);
            return FindIndexedAssetPath(GetSnapshot(), key) != null;
        }

        return (Resolve, Exists);
    }

    private static string NormalizeKey(string path)
    {
        string key = NormalizePath(path);
        if (key.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            key = key["assets/".Length..];
        return key;
    }

    private static IndexSnapshot BuildSnapshot(IEnumerable<string> assets, int version)
    {
        Dictionary<string, string> exactAssets = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> dashedAssets = new(StringComparer.OrdinalIgnoreCase);

        foreach (string asset in assets)
        {
            string normalized = NormalizePath(asset);
            exactAssets[normalized] = asset;
            if (!asset.EndsWith(".slang", StringComparison.OrdinalIgnoreCase))
                continue;

            string moduleName = Path.GetFileName(asset).Replace('_', '-');
            dashedAssets[moduleName] = asset;
        }

        return new IndexSnapshot(version, exactAssets, dashedAssets);
    }

    private static string? FindIndexedAssetPath(IndexSnapshot snapshot, string key)
    {
        if (snapshot.ExactAssets.TryGetValue(key, out string? exact))
            return exact;

        if (!key.EndsWith(".slang", StringComparison.OrdinalIgnoreCase))
            return null;

        string dashed = key.Replace('/', '-').Replace('_', '-');
        if (snapshot.DashedAssets.TryGetValue(dashed, out string? directMatch))
            return directMatch;

        for (int i = 0; i < dashed.Length - 1; i++)
        {
            if (dashed[i] != '-')
                continue;
            string candidate = dashed[(i + 1)..];
            if (snapshot.DashedAssets.TryGetValue(candidate, out string? suffixMatch))
                return suffixMatch;
        }

        return null;
    }

    private sealed class IndexSnapshot(
        int version,
        Dictionary<string, string> exactAssets,
        Dictionary<string, string> dashedAssets)
    {
        public int Version { get; } = version;
        public Dictionary<string, string> ExactAssets { get; } = exactAssets;
        public Dictionary<string, string> DashedAssets { get; } = dashedAssets;
    }

    private static string? ProbeDashed(
        string key, Func<IEnumerable<string>> listNames, Func<string, Stream?> openStream)
    {
        string dashed = key.Replace('/', '-').Replace('_', '-');
        string? match = null;
        int matchedLength = 0;
        foreach (string asset in listNames())
        {
            if (!asset.EndsWith(".slang", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Match the complete file name: ShadowGpuCull must never resolve to
            // AmbientShadowGpuCull. A probe may carry a relative directory prefix
            // or use '/' instead of '_' inside the module name.
            string moduleName = Path.GetFileName(asset).Replace('_', '-');
            if (moduleName.Length <= matchedLength ||
                !dashed.EndsWith(moduleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int prefixLength = dashed.Length - moduleName.Length;
            if (prefixLength > 0 && dashed[prefixLength - 1] != '-')
            {
                continue;
            }

            using Stream? stream = openStream(asset);
            if (stream == null)
            {
                continue;
            }

            // Prefer the full module name over a shorter suffix regardless of
            // asset enumeration order (AlcoRendering_Core before Core).
            match = ReadAll(stream);
            matchedLength = moduleName.Length;
        }
        return match;
    }

    private static string ReadAll(Stream stream)
    {
        using StreamReader reader = new(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    internal static string NormalizePath(string path)
    {
        string slash = path.Replace('\\', '/');
        string prefix = string.Empty;
        string remainder = slash;
        if (slash.Length >= 2 && char.IsLetter(slash[0]) && slash[1] == ':')
        {
            bool hasRootSlash = slash.Length >= 3 && slash[2] == '/';
            prefix = hasRootSlash ? slash[..3] : slash[..2] + "/";
            remainder = hasRootSlash ? slash[3..] : slash[2..];
        }
        else if (slash.StartsWith("//", StringComparison.Ordinal))
        {
            prefix = "//";
            remainder = slash[2..];
        }
        else if (slash.StartsWith('/'))
        {
            prefix = "/";
            remainder = slash[1..];
        }

        bool rooted = prefix.Length != 0;
        List<string> segments = [];
        foreach (string segment in remainder.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count > 0 && segments[^1] != "..")
                    segments.RemoveAt(segments.Count - 1);
                else if (!rooted)
                    segments.Add(segment);
                continue;
            }
            segments.Add(segment);
        }
        string combined = string.Join("/", segments);
        return prefix.Length != 0 ? prefix + combined : combined;
    }
}
