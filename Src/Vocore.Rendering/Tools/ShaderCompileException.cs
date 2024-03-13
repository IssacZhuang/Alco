namespace Vocore.Rendering;

public class ShaderCompilationException : Exception
{
    public int Line { get; }
    public string Name { get; }
    public string ShaderText { get; }
    public new Exception InnerException { get; }
    public ShaderCompilationException(string name, string shaderText, int line, Exception innerException)
    {
        Name = name;
        ShaderText = shaderText;
        Line = line;
        InnerException = innerException;
    }

    public override string ToString()
    {
        // format like F:\Vocore\Src\Vocore.Rendering\Tools\ShaderCompiler.cs(10) for easy navigation
        return $"Shader compilation error in {Name}({Line}): {InnerException.Message}";
    }
}