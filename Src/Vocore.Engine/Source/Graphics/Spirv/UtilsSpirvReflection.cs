using System;
using Silk.NET.SPIRV.Cross;
using Silk.NET.SPIRV.Reflect;
using Veldrid;

namespace Vocore.Engine
{
    public static class UtilsSpirvReflection
    {
        private static readonly Reflect _reflect = Reflect.GetApi();
        public static Reflect Reflect => _reflect;

        public static void SetShaderStage(DescriptorBinding binding, ShaderStage stage)
        {
            binding.Stages |= stage;
        }

        public static void SetShaderStage(DescriptorBinding[] bindings, ShaderStage stage)
        {
            foreach (DescriptorBinding binding in bindings)
            {
                SetShaderStage(binding, stage);
            }
        }

        public static void SetShaderStage(DescriptorSet set, ShaderStage stage)
        {
            foreach (DescriptorBinding binding in set.Bindings)
            {
                SetShaderStage(binding, stage);
            }
        }

        public static DescriptorBinding[] MergeDescriptorBindings(params DescriptorBinding[][] bindingsList)
        {
            Dictionary<uint, DescriptorBinding> bindings = new Dictionary<uint, DescriptorBinding>();
            foreach (DescriptorBinding[] bindingList in bindingsList)
            {
                foreach (DescriptorBinding binding in bindingList)
                {
                    if (bindings.TryGetValue(binding.Binding, out DescriptorBinding existingBinding))
                    {
                        existingBinding.Stages |= binding.Stages;
                        bindings[binding.Binding] = existingBinding;
                    }
                    else
                    {
                        bindings.Add(binding.Binding, binding);
                    }
                    
                }
            }

            return bindings.Values.ToArray();
        }

        
    }
}