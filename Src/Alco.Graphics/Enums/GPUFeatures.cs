namespace Alco.Graphics;

/// <summary>
/// The optional GPU features an adapter may support. Query them with <see cref="GPUDevice.IsFeatureSupported"/>.
/// </summary>
[Flags]
public enum GPUFeatures
{
    None = 0,

    /// <summary>BC-family compressed texture formats (e.g. BC3).</summary>
    TextureCompressionBC = 1 << 0,

    /// <summary>GPU timestamp queries.</summary>
    TimestampQuery = 1 << 1,

    /// <summary>Timestamp writes inside an open render or compute pass, not only at pass boundaries.</summary>
    TimestampQueryInsidePasses = 1 << 2,

    /// <summary>
    /// The device can consume precompiled Metal libraries: wgpu-native was
    /// built with the metallib passthrough entry and the backend is Metal.
    /// </summary>
    MetalLibPassthrough = 1 << 3,

    /// <summary>
    /// Indirect draw records may carry a non-zero firstInstance field (wgpu's
    /// IndirectFirstInstance). Required by instance-step vertex-buffer draws that
    /// address their per-draw data through firstInstance.
    /// </summary>
    IndirectFirstInstance = 1 << 4,

    /// <summary>
    /// One command draws many indirect records: wgpu-native's MultiDrawIndirect.
    /// Requires <see cref="IndirectFirstInstance"/> in practice (the records of a
    /// batch address data through firstInstance).
    /// </summary>
    MultiDrawIndirect = 1 << 5,
}
