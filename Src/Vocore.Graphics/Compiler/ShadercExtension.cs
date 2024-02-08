using System.Text;
using Silk.NET.Shaderc;

namespace Vocore.Graphics;

public static class ShadercExtension
{
    public unsafe static void CompileOptionsAddMacroDefinition(this Shaderc api, CompileOptions* options, string name, string value)
    {
        api.CompileOptionsAddMacroDefinition(options, name, GetStringSize(name), value, GetStringSize(value));
    }

    public unsafe static CompilationResult* CompileIntoSpv(this Shaderc api, Compiler* compiler, string source, ShaderKind kind, string filename, string entry, CompileOptions* options)
    {
        return api.CompileIntoSpv(compiler, source, GetStringSize(source), kind, filename, entry, options);
    }

    private static nuint GetStringSize(string str)
    {
        return (nuint)Encoding.UTF8.GetByteCount(str);
    }
}