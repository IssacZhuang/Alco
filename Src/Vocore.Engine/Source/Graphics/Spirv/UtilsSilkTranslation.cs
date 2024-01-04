using System;
using Silk.NET.SPIRV.Reflect;

using SilkDescriptorType = Silk.NET.SPIRV.Reflect.DescriptorType;
using SilkDescriptorBinding = Silk.NET.SPIRV.Reflect.DescriptorBinding;

namespace Vocore.Engine
{
    public static class UtilsSilkTranslation
    {
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

        public unsafe static DescriptorBinding GetDescriptorBinding(SilkDescriptorBinding binding)
        {
            // return new DescriptorBinding
            // {
            //     Set = binding.Set,
            //     Binding = binding.Binding,
            //     Type = DescriptorTypeSilkToVocore(binding.DescriptorType),
            //     Name = UtilsInterop.ReadString(binding.Name)
            // };
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
    }
}