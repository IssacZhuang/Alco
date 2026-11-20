// Core Vulkan primitive types and typed dispatchable/non-dispatchable handles.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vulkan;

internal readonly unsafe partial struct PFN_vkVoidFunction : IEquatable<PFN_vkVoidFunction>
{
    public PFN_vkVoidFunction(delegate* unmanaged<void> value) => Value = value;
    public delegate* unmanaged<void> Value { get; }
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PFN_vkVoidFunction other && Equals(other);
    public bool Equals(PFN_vkVoidFunction other) => (nint)Value == (nint)other.Value;
    public override int GetHashCode() => ((nint)Value).GetHashCode();
    public override string ToString() => ((nint)Value).ToString();
    public static implicit operator delegate* unmanaged<void>(PFN_vkVoidFunction from) => from.Value;
    public static implicit operator PFN_vkVoidFunction(delegate* unmanaged<void> from) => new(from);
    public static bool operator ==(PFN_vkVoidFunction left, PFN_vkVoidFunction right) => left.Equals(right);
    public static bool operator !=(PFN_vkVoidFunction left, PFN_vkVoidFunction right) => !left.Equals(right);
}

[StructLayout(LayoutKind.Sequential, Size = 4)]
internal readonly struct VkBool32 : IEquatable<VkBool32>
{
    public static VkBool32 True => new(true);
    public static VkBool32 False => new(false);

    private readonly uint _value;

    public VkBool32(bool boolValue) => _value = boolValue ? 1u : 0u;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(VkBool32 other) => _value == other._value;
    public override bool Equals(object? obj) => obj is VkBool32 rawBool && Equals(rawBool);
    public override int GetHashCode() => _value.GetHashCode();
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(VkBool32 left, VkBool32 right) => left.Equals(right);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(VkBool32 left, VkBool32 right) => !left.Equals(right);
    public static implicit operator bool(VkBool32 value) => value._value != 0;
    public static implicit operator VkBool32(bool boolValue) => new(boolValue);
    public override string ToString() => _value != 0 ? "True" : "False";
}

internal readonly struct VkVersion : IComparable, IComparable<VkVersion>, IEquatable<VkVersion>, IFormattable
{
    public readonly uint Value;

    public static VkVersion Version_1_0 => new(0, 1, 0, 0);
    public static VkVersion Version_1_1 => new(0, 1, 1, 0);
    public static VkVersion Version_1_2 => new(0, 1, 2, 0);
    public static VkVersion Version_1_3 => new(0, 1, 3, 0);
    public static VkVersion Version_1_4 => new(0, 1, 4, 0);

    public VkVersion(uint value) => Value = value;
    public VkVersion(uint variant, uint major, uint minor, uint patch) => Value = (variant << 29) | (major << 22) | (minor << 12) | patch;
    public VkVersion(uint major, uint minor, uint patch) => Value = (major << 22) | (minor << 12) | patch;

    public uint Variant => Value >> 29;
    public uint Major => (Value >> 22) & 0x7Fu;
    public uint Minor => (Value >> 12) & 0x3FFu;
    public uint Patch => Value & 0xFFFu;

    public static bool operator ==(VkVersion left, VkVersion right) => left.Value == right.Value;
    public static bool operator !=(VkVersion left, VkVersion right) => left.Value != right.Value;
    public static bool operator <(VkVersion left, VkVersion right) => left.Value < right.Value;
    public static bool operator <=(VkVersion left, VkVersion right) => left.Value <= right.Value;
    public static bool operator >(VkVersion left, VkVersion right) => left.Value > right.Value;
    public static bool operator >=(VkVersion left, VkVersion right) => left.Value >= right.Value;
    public static implicit operator uint(VkVersion version) => version.Value;

    public int CompareTo(object? obj) => obj is VkVersion other ? CompareTo(other) : throw new ArgumentException($"obj is not an instance of {nameof(VkVersion)}.");
    public int CompareTo(VkVersion other) => Value.CompareTo(other.Value);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is VkVersion other && Equals(other);
    public bool Equals(VkVersion other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => $"{Variant}.{Major}.{Minor}.{Patch}";
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        $"{Variant.ToString(format, formatProvider)}.{Major.ToString(format, formatProvider)}.{Minor.ToString(format, formatProvider)}.{Patch.ToString(format, formatProvider)}";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkInstance : IEquatable<VkInstance>
{
    public VkInstance(nint handle) { Handle = handle; }
    public nint Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkInstance Null => new(0);
    public static implicit operator VkInstance(nint handle) => new(handle);
    public static implicit operator nint(VkInstance handle) => handle.Handle;
    public static bool operator ==(VkInstance left, VkInstance right) => left.Handle == right.Handle;
    public static bool operator !=(VkInstance left, VkInstance right) => left.Handle != right.Handle;
    public bool Equals(VkInstance other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkInstance handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkInstance)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkPhysicalDevice : IEquatable<VkPhysicalDevice>
{
    public VkPhysicalDevice(nint handle) { Handle = handle; }
    public nint Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkPhysicalDevice Null => new(0);
    public static implicit operator VkPhysicalDevice(nint handle) => new(handle);
    public static implicit operator nint(VkPhysicalDevice handle) => handle.Handle;
    public static bool operator ==(VkPhysicalDevice left, VkPhysicalDevice right) => left.Handle == right.Handle;
    public static bool operator !=(VkPhysicalDevice left, VkPhysicalDevice right) => left.Handle != right.Handle;
    public bool Equals(VkPhysicalDevice other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkPhysicalDevice handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkPhysicalDevice)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkDevice : IEquatable<VkDevice>
{
    public VkDevice(nint handle) { Handle = handle; }
    public nint Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkDevice Null => new(0);
    public static implicit operator VkDevice(nint handle) => new(handle);
    public static implicit operator nint(VkDevice handle) => handle.Handle;
    public static bool operator ==(VkDevice left, VkDevice right) => left.Handle == right.Handle;
    public static bool operator !=(VkDevice left, VkDevice right) => left.Handle != right.Handle;
    public bool Equals(VkDevice other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkDevice handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkDevice)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkQueue : IEquatable<VkQueue>
{
    public VkQueue(nint handle) { Handle = handle; }
    public nint Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkQueue Null => new(0);
    public static implicit operator VkQueue(nint handle) => new(handle);
    public static implicit operator nint(VkQueue handle) => handle.Handle;
    public static bool operator ==(VkQueue left, VkQueue right) => left.Handle == right.Handle;
    public static bool operator !=(VkQueue left, VkQueue right) => left.Handle != right.Handle;
    public bool Equals(VkQueue other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkQueue handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkQueue)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkCommandBuffer : IEquatable<VkCommandBuffer>
{
    public VkCommandBuffer(nint handle) { Handle = handle; }
    public nint Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkCommandBuffer Null => new(0);
    public static implicit operator VkCommandBuffer(nint handle) => new(handle);
    public static implicit operator nint(VkCommandBuffer handle) => handle.Handle;
    public static bool operator ==(VkCommandBuffer left, VkCommandBuffer right) => left.Handle == right.Handle;
    public static bool operator !=(VkCommandBuffer left, VkCommandBuffer right) => left.Handle != right.Handle;
    public bool Equals(VkCommandBuffer other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkCommandBuffer handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkCommandBuffer)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkSemaphore : IEquatable<VkSemaphore>
{
    public VkSemaphore(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkSemaphore Null => new(0);
    public static implicit operator VkSemaphore(ulong handle) => new(handle);
    public static implicit operator ulong(VkSemaphore handle) => handle.Handle;
    public static bool operator ==(VkSemaphore left, VkSemaphore right) => left.Handle == right.Handle;
    public static bool operator !=(VkSemaphore left, VkSemaphore right) => left.Handle != right.Handle;
    public bool Equals(VkSemaphore other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkSemaphore handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkSemaphore)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkFence : IEquatable<VkFence>
{
    public VkFence(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkFence Null => new(0);
    public static implicit operator VkFence(ulong handle) => new(handle);
    public static implicit operator ulong(VkFence handle) => handle.Handle;
    public static bool operator ==(VkFence left, VkFence right) => left.Handle == right.Handle;
    public static bool operator !=(VkFence left, VkFence right) => left.Handle != right.Handle;
    public bool Equals(VkFence other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkFence handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkFence)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkDeviceMemory : IEquatable<VkDeviceMemory>
{
    public VkDeviceMemory(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkDeviceMemory Null => new(0);
    public static implicit operator VkDeviceMemory(ulong handle) => new(handle);
    public static implicit operator ulong(VkDeviceMemory handle) => handle.Handle;
    public static bool operator ==(VkDeviceMemory left, VkDeviceMemory right) => left.Handle == right.Handle;
    public static bool operator !=(VkDeviceMemory left, VkDeviceMemory right) => left.Handle != right.Handle;
    public bool Equals(VkDeviceMemory other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkDeviceMemory handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkDeviceMemory)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkBuffer : IEquatable<VkBuffer>
{
    public VkBuffer(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkBuffer Null => new(0);
    public static implicit operator VkBuffer(ulong handle) => new(handle);
    public static implicit operator ulong(VkBuffer handle) => handle.Handle;
    public static bool operator ==(VkBuffer left, VkBuffer right) => left.Handle == right.Handle;
    public static bool operator !=(VkBuffer left, VkBuffer right) => left.Handle != right.Handle;
    public bool Equals(VkBuffer other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkBuffer handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkBuffer)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkBufferView : IEquatable<VkBufferView>
{
    public VkBufferView(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkBufferView Null => new(0);
    public static implicit operator VkBufferView(ulong handle) => new(handle);
    public static implicit operator ulong(VkBufferView handle) => handle.Handle;
    public static bool operator ==(VkBufferView left, VkBufferView right) => left.Handle == right.Handle;
    public static bool operator !=(VkBufferView left, VkBufferView right) => left.Handle != right.Handle;
    public bool Equals(VkBufferView other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkBufferView handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkBufferView)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkImage : IEquatable<VkImage>
{
    public VkImage(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkImage Null => new(0);
    public static implicit operator VkImage(ulong handle) => new(handle);
    public static implicit operator ulong(VkImage handle) => handle.Handle;
    public static bool operator ==(VkImage left, VkImage right) => left.Handle == right.Handle;
    public static bool operator !=(VkImage left, VkImage right) => left.Handle != right.Handle;
    public bool Equals(VkImage other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkImage handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkImage)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkImageView : IEquatable<VkImageView>
{
    public VkImageView(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkImageView Null => new(0);
    public static implicit operator VkImageView(ulong handle) => new(handle);
    public static implicit operator ulong(VkImageView handle) => handle.Handle;
    public static bool operator ==(VkImageView left, VkImageView right) => left.Handle == right.Handle;
    public static bool operator !=(VkImageView left, VkImageView right) => left.Handle != right.Handle;
    public bool Equals(VkImageView other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkImageView handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkImageView)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkShaderModule : IEquatable<VkShaderModule>
{
    public VkShaderModule(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkShaderModule Null => new(0);
    public static implicit operator VkShaderModule(ulong handle) => new(handle);
    public static implicit operator ulong(VkShaderModule handle) => handle.Handle;
    public static bool operator ==(VkShaderModule left, VkShaderModule right) => left.Handle == right.Handle;
    public static bool operator !=(VkShaderModule left, VkShaderModule right) => left.Handle != right.Handle;
    public bool Equals(VkShaderModule other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkShaderModule handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkShaderModule)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkPipelineCache : IEquatable<VkPipelineCache>
{
    public VkPipelineCache(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkPipelineCache Null => new(0);
    public static implicit operator VkPipelineCache(ulong handle) => new(handle);
    public static implicit operator ulong(VkPipelineCache handle) => handle.Handle;
    public static bool operator ==(VkPipelineCache left, VkPipelineCache right) => left.Handle == right.Handle;
    public static bool operator !=(VkPipelineCache left, VkPipelineCache right) => left.Handle != right.Handle;
    public bool Equals(VkPipelineCache other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkPipelineCache handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkPipelineCache)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkPipelineLayout : IEquatable<VkPipelineLayout>
{
    public VkPipelineLayout(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkPipelineLayout Null => new(0);
    public static implicit operator VkPipelineLayout(ulong handle) => new(handle);
    public static implicit operator ulong(VkPipelineLayout handle) => handle.Handle;
    public static bool operator ==(VkPipelineLayout left, VkPipelineLayout right) => left.Handle == right.Handle;
    public static bool operator !=(VkPipelineLayout left, VkPipelineLayout right) => left.Handle != right.Handle;
    public bool Equals(VkPipelineLayout other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkPipelineLayout handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkPipelineLayout)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkRenderPass : IEquatable<VkRenderPass>
{
    public VkRenderPass(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkRenderPass Null => new(0);
    public static implicit operator VkRenderPass(ulong handle) => new(handle);
    public static implicit operator ulong(VkRenderPass handle) => handle.Handle;
    public static bool operator ==(VkRenderPass left, VkRenderPass right) => left.Handle == right.Handle;
    public static bool operator !=(VkRenderPass left, VkRenderPass right) => left.Handle != right.Handle;
    public bool Equals(VkRenderPass other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkRenderPass handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkRenderPass)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkFramebuffer : IEquatable<VkFramebuffer>
{
    public VkFramebuffer(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkFramebuffer Null => new(0);
    public static implicit operator VkFramebuffer(ulong handle) => new(handle);
    public static implicit operator ulong(VkFramebuffer handle) => handle.Handle;
    public static bool operator ==(VkFramebuffer left, VkFramebuffer right) => left.Handle == right.Handle;
    public static bool operator !=(VkFramebuffer left, VkFramebuffer right) => left.Handle != right.Handle;
    public bool Equals(VkFramebuffer other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkFramebuffer handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkFramebuffer)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkPipeline : IEquatable<VkPipeline>
{
    public VkPipeline(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkPipeline Null => new(0);
    public static implicit operator VkPipeline(ulong handle) => new(handle);
    public static implicit operator ulong(VkPipeline handle) => handle.Handle;
    public static bool operator ==(VkPipeline left, VkPipeline right) => left.Handle == right.Handle;
    public static bool operator !=(VkPipeline left, VkPipeline right) => left.Handle != right.Handle;
    public bool Equals(VkPipeline other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkPipeline handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkPipeline)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkDescriptorSetLayout : IEquatable<VkDescriptorSetLayout>
{
    public VkDescriptorSetLayout(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkDescriptorSetLayout Null => new(0);
    public static implicit operator VkDescriptorSetLayout(ulong handle) => new(handle);
    public static implicit operator ulong(VkDescriptorSetLayout handle) => handle.Handle;
    public static bool operator ==(VkDescriptorSetLayout left, VkDescriptorSetLayout right) => left.Handle == right.Handle;
    public static bool operator !=(VkDescriptorSetLayout left, VkDescriptorSetLayout right) => left.Handle != right.Handle;
    public bool Equals(VkDescriptorSetLayout other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkDescriptorSetLayout handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkDescriptorSetLayout)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkSampler : IEquatable<VkSampler>
{
    public VkSampler(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkSampler Null => new(0);
    public static implicit operator VkSampler(ulong handle) => new(handle);
    public static implicit operator ulong(VkSampler handle) => handle.Handle;
    public static bool operator ==(VkSampler left, VkSampler right) => left.Handle == right.Handle;
    public static bool operator !=(VkSampler left, VkSampler right) => left.Handle != right.Handle;
    public bool Equals(VkSampler other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkSampler handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkSampler)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkDescriptorPool : IEquatable<VkDescriptorPool>
{
    public VkDescriptorPool(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkDescriptorPool Null => new(0);
    public static implicit operator VkDescriptorPool(ulong handle) => new(handle);
    public static implicit operator ulong(VkDescriptorPool handle) => handle.Handle;
    public static bool operator ==(VkDescriptorPool left, VkDescriptorPool right) => left.Handle == right.Handle;
    public static bool operator !=(VkDescriptorPool left, VkDescriptorPool right) => left.Handle != right.Handle;
    public bool Equals(VkDescriptorPool other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkDescriptorPool handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkDescriptorPool)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkDescriptorSet : IEquatable<VkDescriptorSet>
{
    public VkDescriptorSet(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkDescriptorSet Null => new(0);
    public static implicit operator VkDescriptorSet(ulong handle) => new(handle);
    public static implicit operator ulong(VkDescriptorSet handle) => handle.Handle;
    public static bool operator ==(VkDescriptorSet left, VkDescriptorSet right) => left.Handle == right.Handle;
    public static bool operator !=(VkDescriptorSet left, VkDescriptorSet right) => left.Handle != right.Handle;
    public bool Equals(VkDescriptorSet other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkDescriptorSet handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkDescriptorSet)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkQueryPool : IEquatable<VkQueryPool>
{
    public VkQueryPool(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkQueryPool Null => new(0);
    public static implicit operator VkQueryPool(ulong handle) => new(handle);
    public static implicit operator ulong(VkQueryPool handle) => handle.Handle;
    public static bool operator ==(VkQueryPool left, VkQueryPool right) => left.Handle == right.Handle;
    public static bool operator !=(VkQueryPool left, VkQueryPool right) => left.Handle != right.Handle;
    public bool Equals(VkQueryPool other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkQueryPool handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkQueryPool)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkCommandPool : IEquatable<VkCommandPool>
{
    public VkCommandPool(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkCommandPool Null => new(0);
    public static implicit operator VkCommandPool(ulong handle) => new(handle);
    public static implicit operator ulong(VkCommandPool handle) => handle.Handle;
    public static bool operator ==(VkCommandPool left, VkCommandPool right) => left.Handle == right.Handle;
    public static bool operator !=(VkCommandPool left, VkCommandPool right) => left.Handle != right.Handle;
    public bool Equals(VkCommandPool other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkCommandPool handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkCommandPool)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkSurfaceKHR : IEquatable<VkSurfaceKHR>
{
    public VkSurfaceKHR(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkSurfaceKHR Null => new(0);
    public static implicit operator VkSurfaceKHR(ulong handle) => new(handle);
    public static implicit operator ulong(VkSurfaceKHR handle) => handle.Handle;
    public static bool operator ==(VkSurfaceKHR left, VkSurfaceKHR right) => left.Handle == right.Handle;
    public static bool operator !=(VkSurfaceKHR left, VkSurfaceKHR right) => left.Handle != right.Handle;
    public bool Equals(VkSurfaceKHR other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkSurfaceKHR handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkSurfaceKHR)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkSwapchainKHR : IEquatable<VkSwapchainKHR>
{
    public VkSwapchainKHR(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkSwapchainKHR Null => new(0);
    public static implicit operator VkSwapchainKHR(ulong handle) => new(handle);
    public static implicit operator ulong(VkSwapchainKHR handle) => handle.Handle;
    public static bool operator ==(VkSwapchainKHR left, VkSwapchainKHR right) => left.Handle == right.Handle;
    public static bool operator !=(VkSwapchainKHR left, VkSwapchainKHR right) => left.Handle != right.Handle;
    public bool Equals(VkSwapchainKHR other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkSwapchainKHR handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkSwapchainKHR)} [0x{Handle.ToString("X")}]";
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal readonly partial struct VkDebugUtilsMessengerEXT : IEquatable<VkDebugUtilsMessengerEXT>
{
    public VkDebugUtilsMessengerEXT(ulong handle) { Handle = handle; }
    public ulong Handle { get; }
    public bool IsNull => Handle == 0;
    public bool IsNotNull => Handle != 0;
    public static VkDebugUtilsMessengerEXT Null => new(0);
    public static implicit operator VkDebugUtilsMessengerEXT(ulong handle) => new(handle);
    public static implicit operator ulong(VkDebugUtilsMessengerEXT handle) => handle.Handle;
    public static bool operator ==(VkDebugUtilsMessengerEXT left, VkDebugUtilsMessengerEXT right) => left.Handle == right.Handle;
    public static bool operator !=(VkDebugUtilsMessengerEXT left, VkDebugUtilsMessengerEXT right) => left.Handle != right.Handle;
    public bool Equals(VkDebugUtilsMessengerEXT other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is VkDebugUtilsMessengerEXT handle && Equals(handle);
    public override int GetHashCode() => Handle.GetHashCode();
    private string DebuggerDisplay => $"{nameof(VkDebugUtilsMessengerEXT)} [0x{Handle.ToString("X")}]";
}

