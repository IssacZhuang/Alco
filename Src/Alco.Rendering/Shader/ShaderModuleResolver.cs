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
            string key = NormalizePath(path);
            if (key.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            {
                key = key["assets/".Length..];
            }

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
