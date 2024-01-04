using System;
using Silk.NET.SPIRV.Cross;
using Silk.NET.SPIRV.Reflect;

namespace Vocore.Engine
{
    public class SpirvShaderSource
    {
        private readonly byte[] _spirv;
        private readonly DescriptorSet[] _descriptorSets;
        private readonly VertexInputLayout[] _vertexLayouts;
        private readonly ShaderStage _stage;


        public unsafe SpirvShaderSource(byte[] spirv, ShaderStage stage)
        {
            _stage = stage;

            Reflect reflect = UtilsSpirvReflection.Reflect;
            ReflectShaderModule _reflectData = default;
            _spirv = spirv;
            fixed (byte* ptr = spirv)
            {
                void* p = ptr;
                reflect.CreateShaderModule((nuint)spirv.Length, p, ref _reflectData);
            }

            _descriptorSets = UtilsSilkTranslation.GetDescriptorSets(_reflectData);
            foreach (DescriptorSet set in _descriptorSets)
            {
                UtilsSpirvReflection.SetShaderStage(set, stage);
            }
        }


    }
}