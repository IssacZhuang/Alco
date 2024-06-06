using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Build.Evaluation.Context;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Silk.NET.Core.Loader;
using Vocore.IO;

namespace Vocore.Engine.Tool;


public class AssetsBuildTask : Microsoft.Build.Utilities.Task
{
    private class NativeLibraryPreloader
    {
        private readonly List<nint> _handles = new List<nint>();
        public bool TryLoad(string path)
        {
            if (NativeLibrary.TryLoad(path, out nint handle))
            {
                _handles.Add(handle);
                return true;
            }

            return false;
        }

        public void FreeAll()
        {
            foreach (nint handle in _handles)
            {
                NativeLibrary.Free(handle);
            }
        }
    }

    [Required]
    public string? AssetsDir { get; set; }

    [Required]
    public string? OutputDir { get; set; }

    [Required]
    public string? PackageName { get; set; }

    public override bool Execute()
    {
        if (string.IsNullOrEmpty(AssetsDir))
        {
            Log.LogError("AssetsDir are required");
            return false;
        }

        if (string.IsNullOrEmpty(OutputDir))
        {
            Log.LogError("OutputDir are required");
            return false;
        }

        if (string.IsNullOrEmpty(PackageName))
        {
            Log.LogError("PackageName are required");
            return false;
        }

        if (!Directory.Exists(AssetsDir))
        {
            Log.LogError($"AssetsDir '{AssetsDir}' does not exist");
            return false;
        }

        if (!Directory.Exists(OutputDir))
        {
            Log.LogError($"OutputDir '{OutputDir}' does not exist");
            return false;
        }

        Log.LogMessage(MessageImportance.High, $"\n-- Building package '{PackageName}' \npacking '{Path.GetFullPath(AssetsDir)}' \nto '{Path.GetFullPath(OutputDir)}'\n");

        NativeLibraryPreloader preloader = new NativeLibraryPreloader();

        preloader.TryLoad(Path.Combine(Path.GetFullPath(OutputDir), "spirv-reflect.dll"));//windows
        preloader.TryLoad(Path.Combine(Path.GetFullPath(OutputDir), "libspirv-reflect.so"));//linux
        preloader.TryLoad(Path.Combine(Path.GetFullPath(OutputDir), "libspirv-reflect.dylib"));//mac

        AssetImportHelper? importHelper = null;
        Package? package = null;
        try
        {
            // preload the native libraries otherwise Silk.NET can not resolved correctly in this task

            // for UtilsShaderRelfection
            
            


            package = new Package();

            DirectoryFileSource source = new DirectoryFileSource(AssetsDir);
            importHelper = new AssetImportHelper(12);
            importHelper.RegisterImporter(new AssertImporterShaderHLSL((string includeName) =>
            {
                if (source.TryGetData(includeName, out ReadOnlySpan<byte> data))
                {
                    return Encoding.UTF8.GetString(data);
                }
                throw new Exception($"Failed to load include file '{includeName}'");
            }));

            bool isSuccessful = true;

            foreach (string filename in source.AllFileNames)
            {
                if (!source.TryGetData(filename, out ReadOnlySpan<byte> data))
                {
                    isSuccessful = false;
                    Log.LogError($"Failed to load file '{filename}'");
                    continue;
                }

                byte[] fileData = data.ToArray();
                // push to import helper or add to package directly
                if (!importHelper.PushFile(filename, fileData))
                {
                    package.AddFile(filename, fileData);
                }
            }

            foreach (AssetImportResult result in importHelper.GetResults())
            {
                if (result.exception == null)
                {
                    package.AddFile(result.ImportedFilename!, result.ImportedFile!);
                    ImportedAssetMeta meta = new ImportedAssetMeta(result.Filename, result.ImportedFilename!);
                    string metaFilename = result.Filename + FileExt.Imported;
                    package.AddFile(metaFilename, Encoding.UTF8.GetBytes(meta.ToJson()));
                }
                else
                {
                    isSuccessful = false;
                    Log.LogError($"Failed to import file '{result.Filename}': {result.exception}");
                }
            }

            package.Write(Path.Combine(OutputDir, PackageName));
            return isSuccessful;
        }
        catch (Exception e)
        {
            Log.LogError(e.ToString());
            return false;
        }
        finally
        {
            package?.Dispose();
            importHelper?.Dispose();
            preloader.FreeAll();
        }
    }
}
