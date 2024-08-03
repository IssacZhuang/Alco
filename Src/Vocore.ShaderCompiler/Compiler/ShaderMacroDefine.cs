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

    public static string BuildMacroString(ShaderMacroDefine[] macros)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(FirstLine);

        foreach (var macro in macros)
        {
            sb.AppendLine(macro.ToString());
        }

        return sb.ToString();
    }

    public static string BuildMacroString(IList<ShaderMacroDefine> macros)
    {
        StringBuilder sb = new StringBuilder();

        foreach (var macro in macros)
        {
            sb.AppendLine(macro.ToString());
        }

        sb.AppendLine(FirstLine);

        return sb.ToString();
    }
}