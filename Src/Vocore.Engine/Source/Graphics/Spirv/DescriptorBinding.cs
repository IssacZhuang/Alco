
namespace Vocore.Engine
{
    /// <summary>
    /// Represents a descriptor binding in a shader program.
    /// </summary>
    public struct DescriptorBinding
    {
        /// <summary>
        /// The set index of the descriptor binding.
        /// </summary>
        public readonly uint Set;

        /// <summary>
        /// The binding index of the descriptor binding.
        /// </summary>
        public readonly uint Binding;

        /// <summary>
        /// The name of the descriptor binding.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The type of the descriptor binding.
        /// </summary>
        public readonly DescriptorType Type;

        /// <summary>
        /// The shader stages in which the descriptor binding is used.
        /// </summary>
        public ShaderStage Stages;

        /// <summary>
        /// Initializes a new instance of the <see cref="DescriptorBinding"/> struct.
        /// </summary>
        /// <param name="set">The set index of the descriptor binding.</param>
        /// <param name="binding">The binding index of the descriptor binding.</param>
        /// <param name="name">The name of the descriptor binding.</param>
        /// <param name="type">The type of the descriptor binding.</param>
        /// <param name="stages">The shader stages in which the descriptor binding is used.</param>
        public DescriptorBinding(uint set, uint binding, string name, DescriptorType type, ShaderStage stages)
        {
            Set = set;
            Binding = binding;
            Name = name;
            Type = type;
            Stages = stages;
        }

        /// <summary>
        /// Returns a string representation of the descriptor binding.
        /// </summary>
        /// <returns>A string representation of the descriptor binding.</returns>
        public override string ToString()
        {
            return $"Set: {Set}, Binding: {Binding}, Name: {Name}, Type: {Type}, Stages: {Stages}";
        }
    }
}