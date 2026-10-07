namespace Alco.Graphics
{
    /// <summary>
    /// Represents the information required to create a shader stage.
    /// </summary>
    public struct ShaderModule
    {
        /// <summary>
        /// An empty module representing "no shader" for a stage.
        /// </summary>
        public static readonly ShaderModule Empty = new(ShaderStage.None, ShaderLanguage.Undefined, Array.Empty<byte>(), string.Empty);

        /// <summary>
        /// Initializes the module with its stage, language, source bytes and entry
        /// point name.
        /// </summary>
        /// <param name="stage">The shader stage.</param>
        /// <param name="language">The shader language.</param>
        /// <param name="source">The shader source code.</param>
        /// <param name="entryPoint">The entry point function name.</param>
        public ShaderModule(ShaderStage stage, ShaderLanguage language, ReadOnlyMemory<byte> source, string entryPoint)
        {
            Stage = stage;
            Language = language;
            Source = source;
            EntryPoint = entryPoint;
        }

        /// <summary>
        /// Validates that the module carries a non-empty source and entry point.
        /// </summary>
        public readonly void Validate()
        {
            AssetUtility.IsTrue(Source.Length > 0, "Shader source must not be null or empty");
            AssetUtility.IsTrue(!string.IsNullOrEmpty(EntryPoint), "Shader entry point must not be null or empty");
        }

        /// <summary>
        /// The pipeline stage this module feeds (Vertex, Fragment, Compute).
        /// </summary>
        public ShaderStage Stage { get; init; }

        /// <summary>
        /// The language or IR of <see cref="Source"/> (HLSL, WGSL, SPIR-V...).
        /// </summary>
        public ShaderLanguage Language { get; init; }

        /// <summary>
        /// The shader source bytes: source code (HLSL, WGSL) or IR (SPIR-V).
        /// </summary>
        public ReadOnlyMemory<byte> Source { get; init; }

        /// <summary>
        /// The entry function name inside the module.
        /// </summary>
        public string EntryPoint { get; init; }

        /// <summary>
        /// The compute threadgroup size ([numthreads]) carried for passthrough targets
        /// that cannot reflect it from submitted code (DXIL/MSL). Graphics stages ignore it.
        /// </summary>
        public (uint X, uint Y, uint Z) WorkgroupSize { get; init; } = (1, 1, 1);

        /// <summary>
        /// Whether the SPIR-V source already uses Naga's internal coordinate
        /// convention (Slang's direct emission), so the GL-style Y adjustment must
        /// be skipped when translating it. Only meaningful for SPIR-V consumed
        /// through translation (D3D12); defaults to false, which keeps externally
        /// produced GLSL-style SPIR-V behaving like stock wgpu.
        /// </summary>
        public bool SpirvAdjustedCoordinates { get; init; }
    }
}