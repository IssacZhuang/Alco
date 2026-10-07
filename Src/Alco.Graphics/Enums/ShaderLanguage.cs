namespace Alco.Graphics
{
    /// <summary>
    /// The language or IR a shader module's bytes are written in.
    /// </summary>
    public enum ShaderLanguage
    {
        /// <summary>No language declared.</summary>
        Undefined,
        /// <summary>Slang source code.</summary>
        SLANG,
        /// <summary>SPIR-V binary IR.</summary>
        SPIRV,
        /// <summary>WGSL source code.</summary>
        WGSL,
        /// <summary>DXIL binary containers (HLSL-derived, for D3D12).</summary>
        DXIL,
        /// <summary>Metal Shading Language source code (for Metal).</summary>
        MSL,
        /// <summary>
        /// Precompiled Metal library containers (.metallib), consumed without
        /// compilation unlike <see cref="MSL"/> source.
        /// </summary>
        MetalLib
    }
}
