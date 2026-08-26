using Alco.ShaderCompiler;

namespace Alco.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// Module-name → asset-path resolution (plan D1/D5): slang probes a module name
// ('AlcoRendering_Core') as several file forms ('AlcoRendering/Core.slang',
// 'AlcoRendering-Core.slang', 'AlcoRendering_Core.slang'). The engine resolver
// answers those probes against the asset system by comparing dashed forms, so a
// module's file can live anywhere under Assets/ (Libs/, Passes/…) without
// its directory position being load-bearing.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Builds slang module resolvers over a name-addressed asset source.</summary>
public static class ShaderModuleResolver
{
    /// <param name="openStream">Opens an asset by its exact asset-system path.</param>
    /// <param name="listNames">Lists all asset names (used for probe matching).</param>
    public static SlangFileResolver Create(
        Func<string, Stream?> openStream,
        Func<IEnumerable<string>> listNames)
    {
        return path =>
        {
            string key = SlangPathUtility.NormalizePath(path);
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
            //    position is never load-bearing (plan D5).
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
            foreach (string asset in listNames())
            {
                if (!asset.EndsWith(".slang", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string assetDashed = asset.Replace('/', '-').Replace('_', '-');
                if (dashed.EndsWith(assetDashed, StringComparison.OrdinalIgnoreCase) ||
                    assetDashed.EndsWith(dashed, StringComparison.OrdinalIgnoreCase))
                {
                    Stream? stream = openStream(asset);
                    if (stream != null)
                    {
                        using (stream)
                        {
                            return ReadAll(stream);
                        }
                    }
                }
            }
            return null;
    }

    private static string ReadAll(Stream stream)
    {
        using StreamReader reader = new(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
