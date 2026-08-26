using CppAst;


partial class CsCodeGenerator
{
    private void GenerateConstants(CppCompilation compilation)
    {
        string visibility = _options.PublicVisiblity ? "public" : "internal";
        using var writer = new CodeWriter(Path.Combine(_options.OutputPath, "Constants.cs"), false, _options.Namespace, Array.Empty<string>());
        using (writer.PushBlock($"{visibility} static partial class {_options.ClassName}"))
        {
            foreach (CppMacro cppMacro in compilation.Macros)
            {
                if (string.IsNullOrEmpty(cppMacro.Value)
                    // Skip internal C-only helper macros and anything that expands to them (e.g. the *_INIT struct initializers)
                    || cppMacro.Name.StartsWith("_wgpu_", StringComparison.Ordinal)
                    || cppMacro.Value.Contains("_wgpu_", StringComparison.Ordinal)
                    || _options.ExcludeConstants.Contains(cppMacro.Name)
                    || cppMacro.Name.EndsWith("_H_", StringComparison.OrdinalIgnoreCase)
                    || cppMacro.Name.Equals("WGPU_EXPORT", StringComparison.OrdinalIgnoreCase)
                    || cppMacro.Name.Equals("WGPU_SHARED_LIBRARY", StringComparison.OrdinalIgnoreCase)
                    || cppMacro.Name.Equals("WGPU_IMPLEMENTATION", StringComparison.OrdinalIgnoreCase)
                    || cppMacro.Name.Equals("_wgpu_COMMA", StringComparison.OrdinalIgnoreCase)
                    || cppMacro.Name.Equals("_wgpu_MAKE_INIT_STRUCT", StringComparison.OrdinalIgnoreCase)
                    || cppMacro.Name.Equals("WGPU_STRING_VIEW_INIT", StringComparison.OrdinalIgnoreCase)
                    || cppMacro.Name.Equals("WGPU_STRLEN", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                string modifier = "const";
                string macroValue = NormalizeEnumValue(cppMacro.Value, out string csDataType);

                switch (cppMacro.Name)
                {
                    case "WGPU_TRUE":
                        macroValue = "1u";
                        break;
                    case "WGPU_FALSE":
                        macroValue = "0u";
                        break;
                    case "WGPU_DEPTH_CLEAR_VALUE_UNDEFINED":
                        modifier = "static readonly";
                        csDataType = "float";
                        macroValue = "float.NaN";
                        break;
                    case "WGPU_WHOLE_MAP_SIZE":
                        modifier = "static readonly";
                        csDataType = "nuint";
                        macroValue = "nuint.MaxValue";
                        break;
                    case "WGPU_WHOLE_SIZE":
                    case "WGPU_LIMIT_U64_UNDEFINED":
                        csDataType = "ulong";
                        macroValue = "0xffffffffffffffff";
                        break;
                    case "WGPU_ARRAY_LAYER_COUNT_UNDEFINED":
                    case "WGPU_COPY_STRIDE_UNDEFINED":
                    case "WGPU_DEPTH_SLICE_UNDEFINED":
                    case "WGPU_LIMIT_U32_UNDEFINED":
                    case "WGPU_MIP_LEVEL_COUNT_UNDEFINED":
                    case "WGPU_QUERY_SET_INDEX_UNDEFINED":
                        macroValue = "0xffffffffu";
                        break;
                }

                writer.WriteLine($"/// <unmanaged>{cppMacro.Name}</unmanaged>");
                writer.WriteLine($"public {modifier} {csDataType} {cppMacro.Name} = {macroValue};");
            }
        }
    }
}
