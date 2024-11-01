using System.Text;

namespace Vocore.ShaderCompiler;

public struct ShaderMacroDefine
{
    public const string FirstLine = "#line 1";
    public string name;
    public string value;
    public ShaderMacroDefine(string name, string value)
    {
        this.name = name;
        this.value = value;
    }

    public override string ToString()
    {
        return $"#define {name} {value}";
    }
}