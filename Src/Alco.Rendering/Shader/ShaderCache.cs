using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Text;

namespace Alco.Rendering;

public unsafe class ShaderCache : IShaderCache
{
    private readonly string _directory;

    public ShaderCache(string directory)
    {
        _directory = directory;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public Task<Exception?> AddOrUpdateAsync(string path, string shaderText, ReadOnlySpan<string> defines, ShaderModulesInfo modulesInfo)
    {
        string cachePath = GetCachePath(path, defines);

        return Task.Run(() =>
        {
            Exception? exception = null;

            try
            {
                string? directory = Path.GetDirectoryName(cachePath);
                if (directory != null)
                {
                    Directory.CreateDirectory(directory);
                }

                // Write to a temp file then atomically move into place. NUnit runs
                // tests in parallel, so several engine instances may try to populate
                // the same cache entry at once; writing directly with File.Create
                // (truncating) would corrupt the file under concurrent writers.
                string tempPath = cachePath + ".tmp";

                using (FileStream stream = File.Create(tempPath))
                using (BinaryWriter writer = new(stream))
                {
                    ulong hash = GetHash(shaderText);
                    writer.Write(hash);

                    ReadOnlyMemory<byte> bytes = ShaderUtility.EncodeShaderModulesInfo(modulesInfo);
                    writer.Write(bytes.Span);
                }

                File.Move(tempPath, cachePath, overwrite: true);

                Log.Info("Shader save to cache: ", cachePath);
            }
            catch (Exception e)
            {
                exception = e;
            }

            return exception;
        });
    }

    public bool TryGetModules(string path, string shaderText, ReadOnlySpan<string> defines, [NotNullWhen(true)] out ShaderModulesInfo? modulesInfo)
    {
        string cachePath = GetCachePath(path, defines);
        if (!File.Exists(cachePath))
        {
            modulesInfo = null;
            return false;
        }

        byte* ptrData = null;
        try
        {
            ulong hash = GetHash(shaderText);
            using FileStream stream = File.OpenRead(cachePath);
            using BinaryReader reader = new(stream);

            ulong cacheHash = reader.ReadUInt64();
            if (cacheHash != hash)
            {
                modulesInfo = null;
                return false;
            }

            //read rest of the into a byte[]
            int length = (int)stream.Length - 8;
            ptrData = (byte*)MemoryUtility.Alloc(length);
            Span<byte> bytes = new(ptrData, length);

            reader.Read(bytes);
            modulesInfo = ShaderUtility.DecodeShaderModulesInfo(bytes);
            // The cache re-reflects the modules from SPIR-V, which carries no
            // comparison sampler marker; re-apply the markers from the shader text.
            ShaderUtility.MarkDepthComparisonSamplers(modulesInfo, shaderText);
        }
        catch (Exception e)
        {
            Log.Error("Error loading shader cache: ", e);
            modulesInfo = null;
            return false;
        }
        finally
        {
            if (ptrData != null)
            {
                MemoryUtility.Free(ptrData);
            }
        }
        Log.Info("Shader load from cache: ", cachePath);
        return true;
    }

    private string GetCachePath(string path, ReadOnlySpan<string> defines)
    {
        string pathWithoutExtension = Path.ChangeExtension(path, null);
        string definesHash = string.Join("_", defines!);
        return Path.Combine(_directory, $"{pathWithoutExtension}_{definesHash}.cache");
    }

    private static ulong GetHash(string shaderText)
    {
        var bytes = Encoding.UTF8.GetBytes(shaderText);
        var hash = XxHash64.HashToUInt64(bytes);
        return hash;
    }
}