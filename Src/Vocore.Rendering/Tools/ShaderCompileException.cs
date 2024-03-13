namespace Vocore.Rendering;

/// <summary>
/// Global shader compilation exception.
/// </summary>
public class ShaderCompilationException : Exception
{
    
}

public class ShaderValidationException : ShaderCompilationException
{
    public new string Message { get; }
    public ShaderValidationException(string message)
    {
        Message = message;
    }

    public override string ToString()
    {
        return $"Shader validation error: {Message}";
    }
}


public class ShaderLineException : ShaderCompilationException
{
    public int Line { get; }
    public string Name { get; }
    public string ShaderText { get; }
    public new Exception InnerException { get; }

    public ShaderLineException(string name, string shaderText, int line, Exception innerException)
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