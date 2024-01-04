using System;
using System.Text;
using Veldrid;

namespace Vocore.Engine
{
    /// <summary>
    /// Represents a descriptor set in a shader program.
    /// </summary>
    public readonly struct DescriptorSet
    {
        /// <summary>
        /// The index of the descriptor set.
        /// </summary>
        public readonly uint Set;

        /// <summary>
        /// An array of descriptor bindings associated with the descriptor set.
        /// </summary>
        public readonly DescriptorBinding[] Bindings;

        /// <summary>
        /// Initializes a new instance of the <see cref="DescriptorSet"/> struct.
        /// </summary>
        /// <param name="set">The index of the descriptor set.</param>
        /// <param name="bindings">An array of descriptor bindings.</param>
        public DescriptorSet(uint set, DescriptorBinding[] bindings)
        {
            Set = set;
            Bindings = bindings;
        }

        /// <summary>
        /// Returns a string representation of the descriptor set.
        /// </summary>
        /// <returns>A string representation of the descriptor set.</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Set: {Set}");
            foreach (DescriptorBinding binding in Bindings)
            {
                sb.AppendLine(binding.ToString());
            }
            return sb.ToString();
        }
    }
}