namespace Vocore.Graphics
{
    /// <summary>
    /// Represents the information required to create a shader stage.
    /// </summary>
    public struct ShaderStageCreateInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ShaderStageCreateInfo"/> struct.
        /// </summary>
        /// <param name="stage">The shader stage.</param>
        /// <param name="language">The shader language.</param>
        /// <param name="source">The shader source code.</param>
        /// <param name="entryPoint">The entry point function name.</param>
        public ShaderStageCreateInfo(ShaderStage stage, ShaderLanguage language, byte[] source, string entryPoint)
        {
            Stage = stage;
            Language = language;
            Source = source;
            EntryPoint = entryPoint;
        }

        /// <summary>
        /// The shader stage.
        /// </summary>
        public ShaderStage Stage { get; init; }

        /// <summary>
        /// The shader language.
        /// </summary>
        public ShaderLanguage Language { get; init; }

        /// <summary>
        /// The shader source, could be code(hlsl, wgsl) or IR(spirv)
        /// </summary>
        public byte[] Source { get; init; }

        /// <summary>
        /// The entry point function name.
        /// </summary>
        public string EntryPoint { get; init; }
    }
}