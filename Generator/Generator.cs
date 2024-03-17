using System.Text;
using Vocore;
using Vocore.Engine;
using Vocore.Rendering;

public class Generator : GameEngine
{
    private struct ShaderFile
    {
        public string filename;
        public string code;
    }
    private DirectoryFileSource _source;
    public Generator(GameEngineSetting setting) : base(setting)
    {
        _source = new DirectoryFileSource("Assets");
    }

    protected override void OnStart()
    {
        IEnumerable<string> hlslFiles = _source.AllFileNames.Where(x => x.EndsWith(".hlsl"));

        List<ShaderFile> hlslCodes = new List<ShaderFile>();
        foreach (var file in hlslFiles)
        {
            if (_source.TryGetData(file, out byte[]? data))
            {
                hlslCodes.Add(new ShaderFile
                {
                    filename = file,
                    code = Encoding.UTF8.GetString(data)
                });
            }
        }

        List<ShaderCompileResult> shaderCompileResults = new List<ShaderCompileResult>();
        foreach (var shader in hlslCodes)
        {
            Log.Info($"Compiling {shader.filename}");
            ShaderCompileResult result = ShaderCompiler.Compile(shader.code, shader.filename, IncludeResolver);
        }


        Stop();
    }

    private string IncludeResolver(string includePath)
    {
        if (_source.TryGetData(includePath, out byte[]? data))
        {
            return Encoding.UTF8.GetString(data);
        }
        throw new FileNotFoundException($"File {includePath} not found");
    }
}