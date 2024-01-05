using System;
using Silk.NET.SPIRV.Reflect;

using SilkDescriptorType = Silk.NET.SPIRV.Reflect.DescriptorType;
using SilkDescriptorBinding = Silk.NET.SPIRV.Reflect.DescriptorBinding;
using SilkVertexSematic = Silk.NET.SPIRV.BuiltIn;

namespace Vocore.Engine
{
    public static class UtilsSilkTranslation
    {
        private static readonly Dictionary<Format, VertexFormat> SilkVertexFormatToVocore = new Dictionary<Format, VertexFormat>{
            {Format.R16G16Uint, VertexFormat.UShort2},
            {Format.R16G16B16A16Uint, VertexFormat.Short4},
            {Format.R16G16Sint, VertexFormat.Short2},
            {Format.R16G16B16A16Sint, VertexFormat.Short2},
            {Format.R16G16Sfloat, VertexFormat.Half2},
            {Format.R16G16B16A16Sfloat, VertexFormat.Half4},
            {Format.R32Uint, VertexFormat.UInt1},
            {Format.R32G32Uint, VertexFormat.UInt2},
            {Format.R32G32B32Uint, VertexFormat.UInt3},
            {Format.R32G32B32A32Uint, VertexFormat.UInt4},
            {Format.R32Sint, VertexFormat.Int1},
            {Format.R32G32Sint, VertexFormat.Int2},
            {Format.R32G32B32Sint, VertexFormat.Int3},
            {Format.R32G32B32A32Sint, VertexFormat.Int4},
            {Format.R32Sfloat, VertexFormat.Float1},
            {Format.R32G32Sfloat, VertexFormat.Float2},
            {Format.R32G32B32Sfloat, VertexFormat.Float3},
            {Format.R32G32B32A32Sfloat, VertexFormat.Float4},
        };

        public static DescriptorType DescriptorTypeSilkToVocore(SilkDescriptorType type)
        {
            switch (type)
            {
                case SilkDescriptorType.Sampler:
                    return DescriptorType.Sampler;
                case SilkDescriptorType.CombinedImageSampler:
                    return DescriptorType.CombinedImageSampler;
                case SilkDescriptorType.SampledImage:
                    return DescriptorType.SampledImage;
                case SilkDescriptorType.StorageImage:
                    return DescriptorType.StorageImage;
                case SilkDescriptorType.UniformTexelBuffer:
                    return DescriptorType.UniformTexelBuffer;
                case SilkDescriptorType.StorageTexelBuffer:
                    return DescriptorType.StorageTexelBuffer;
                case SilkDescriptorType.UniformBuffer:
                    return DescriptorType.UniformBuffer;
                case SilkDescriptorType.StorageBuffer:
                    return DescriptorType.StorageBuffer;
                case SilkDescriptorType.UniformBufferDynamic:
                    return DescriptorType.UniformBufferDynamic;
                case SilkDescriptorType.StorageBufferDynamic:
                    return DescriptorType.StorageBufferDynamic;
                case SilkDescriptorType.InputAttachment:
                    return DescriptorType.InputAttachment;
                default:
                    throw new Exception($"Unknown descriptor type {type}");
            }
        }

        public static VertexFormat VertexFormatSilkToVocore(Format format)
        {
            if (SilkVertexFormatToVocore.TryGetValue(format, out VertexFormat vocoreFormat))
            {
                return vocoreFormat;
            }
            else
            {
                throw new Exception($"Unknown vertex format {format}");
            }
        }

        public unsafe static DescriptorBinding GetDescriptorBinding(SilkDescriptorBinding binding)
        {
            return new DescriptorBinding(binding.Set, binding.Binding, UtilsInterop.ReadString(binding.Name), DescriptorTypeSilkToVocore(binding.DescriptorType), ShaderStage.None);
        }

        public unsafe static DescriptorBinding[] GetDescriptorBindings(ReflectDescriptorSet set)
        {
            DescriptorBinding[] bindings = new DescriptorBinding[set.BindingCount];
            for (int i = 0; i < set.BindingCount; i++)
            {
                bindings[i] = GetDescriptorBinding(*set.Bindings[i]);
            }
            return bindings;
        }

        public unsafe static DescriptorSet GetDescriptorSet(ReflectDescriptorSet set)
        {
            // return new DescriptorSet
            // {
            //     Set = set.Set,
            //     Bindings = GetDescriptorBindings(set)
            // };
            return new DescriptorSet(set.Set, GetDescriptorBindings(set));
        }

        public unsafe static DescriptorSet[] GetDescriptorSets(ReflectShaderModule shaderModule)
        {
            DescriptorSet[] sets = new DescriptorSet[shaderModule.DescriptorSetCount];
            for (int i = 0; i < shaderModule.DescriptorSetCount; i++)
            {
                sets[i] = GetDescriptorSet(shaderModule.DescriptorSets[i]);
            }
            return sets;
        }

        public unsafe static VertexElement GetVertexElement(InterfaceVariable input)
        {
            return new VertexElement
            {
                Name = UtilsInterop.ReadString(input.Name),
                Format = VertexFormatSilkToVocore(input.Format),
                Offset = input.Location
            };
        }

        public unsafe static VertexElement[] GetVertexElements(InterfaceVariable** inputs, uint count, out uint stride)
        {
            stride = 0;
            VertexElement[] elements = new VertexElement[count];
            for (int i = 0; i < count; i++)
            {
                InterfaceVariable* input = inputs[i];
                elements[i] = GetVertexElement(*input);
                stride += GetNumericSize(input->Numeric);
            }

            return elements;
        }

        public unsafe static VertexInputLayout GetVertexInputLayout(ReflectShaderModule shaderModule)
        {
            VertexElement[] elements = GetVertexElements(shaderModule.InputVariables, shaderModule.InputVariableCount, out uint stride);
            return new VertexInputLayout(elements, stride, VertexStepMode.Vertex);
        }

        public static uint GetNumericSize(NumericTraits num)
        {
            return num.Scalar.Width / 8 * num.Vector.ComponentCount;
        }
    }
}