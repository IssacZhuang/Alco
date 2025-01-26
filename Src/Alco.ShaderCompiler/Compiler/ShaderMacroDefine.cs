using System.Text;

namespace Alco.ShaderCompiler;

public struct ShaderMacroDefine
{
    public const string FirstLine = "#line 1";
    public string Name;
    public string Value;
    public ShaderMacroDefine(string name, string value)
    {
        this.Name = name;
        this.Value = value;
    }

    public override string ToString()
    {
        return $"#define {Name} {Value}";
    }
}