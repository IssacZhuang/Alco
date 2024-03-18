using System.Text;
using Vocore;
using Vocore.Engine;
using Vocore.Rendering;

public class Generator : GameEngine
{
    private const string ExtensionCompiledShader = ".scb"; //shader compile binary
    private const string OutputVocoreRenderingAssetsFolder = "Src/Vocore.Rendering/Assets/";
    private const string CompiledShaderFolder = "CompiledShaders";
    private const string ShaderLibFolder = "ShaderLib";
    

    private DirectoryFileSource _source;
    private string _solutionFolder;
    public Generator(GameEngineSetting setting) : base(setting)
    {
        _source = new DirectoryFileSource("Assets");
        if (!GetSolutionFolder(out _solutionFolder))
        {
            throw new Exception("Unable to find solution folder");
        }
    }

    protected override void OnStart()
    {
        string outputPath = Path.Combine(_solutionFolder, OutputVocoreRenderingAssetsFolder, CompiledShaderFolder);
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }
        else
        {
            foreach (var file in Directory.GetFiles(outputPath))
            {
                File.Delete(file);
            }
        }

        Log.Info("\nCompiling shaders...");
        IEnumerable<string> hlslFiles = _source.AllFileNames.Where(x => x.EndsWith(".hlsl"));


        List<ShaderCompileResult> shaderCompileResults = new List<ShaderCompileResult>();
        foreach (var filename in hlslFiles)
        {
            if (!_source.TryGetData(filename, out ReadOnlySpan<byte> codeData))
            {
                Log.Error($"Unable to read file {filename}");
                continue;
            }

            string code = Encoding.UTF8.GetString(codeData);

            Log.Info($"Compiling {filename}");
            ShaderCompileResult result = ShaderCompiler.Compile(code, filename, IncludeResolver);
            byte[] data = UtilsShaderSerialization.EncodeCompileResult(result);
            string outputFilename = Path.Combine(outputPath, Path.GetFileNameWithoutExtension(filename) + ExtensionCompiledShader);
            Log.Info($"Saving {outputFilename}");
            File.WriteAllBytes(outputFilename, data);
        }


        Log.Info("\nCopying shader include files...");
        //copy .hlsli files to Vocore.Rendering 
        IEnumerable<string> hlslIncludeFiles = _source.AllFileNames.Where(x => x.EndsWith(".hlsli"));
        string includeOutputPath = Path.Combine(_solutionFolder, OutputVocoreRenderingAssetsFolder, ShaderLibFolder);
        if (!Directory.Exists(includeOutputPath))
        {
            Directory.CreateDirectory(includeOutputPath);
        }

        foreach (var filename in hlslIncludeFiles)
        {
            if (!_source.TryGetData(filename, out ReadOnlySpan<byte> data))
            {
                Log.Error($"Unable to read file {filename}");
                continue;
            }
            string outputFilename = Path.Combine(includeOutputPath, Path.GetFileName(filename));
            Log.Info($"Saving {outputFilename}");
            File.WriteAllBytes(outputFilename, data.ToArray());
        }


        Stop();
    }

    //find the directory contains .sln file in parent directories
    private bool GetSolutionFolder(out string path)
    {
        string? current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            string[] files = Directory.GetFiles(current, "*.sln");
            if (files.Length > 0)
            {
                path = current;
                return true;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        path = "";
        return false;
    }

    private string IncludeResolver(string includePath)
    {
        if (_source.TryGetData(includePath, out ReadOnlySpan<byte> data))
        {
            return Encoding.UTF8.GetString(data);
        }
        throw new FileNotFoundException($"File {includePath} not found");
    }
}