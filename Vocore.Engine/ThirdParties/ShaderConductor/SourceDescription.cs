using System;

namespace Vocore.ShaderConductor
{
    public struct SourceDescription
    {
        public string source;
        public string entryPoint;
        public ShaderStage stage;
    }
}

// [StructLayout(LayoutKind.Sequential)]
// internal unsafe struct Native_SourceDescription
// {
//     public byte* source;
//     public byte* entryPoint;
//     public Native_ShaderStage stage;
// }