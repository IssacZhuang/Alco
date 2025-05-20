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
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                using FileStream stream = File.Create(cachePath);
                using BinaryWriter writer = new(stream);

                ulong hash = GetHash(shaderText);
                writer.Write(hash);

                byte[] bytes = UtilsShader.EncodeShaderModulesInfo(modulesInfo);
                writer.Write(bytes);

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
        byte* ptrData = (byte*)UtilsMemory.Alloc(length);
        Span<byte> bytes = new(ptrData, length);
        try
        {
            reader.Read(bytes);
            modulesInfo = UtilsShader.DecodeShaderModulesInfo(bytes);
        }
        finally
        {
            UtilsMemory.Free(ptrData);
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