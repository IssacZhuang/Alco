namespace Alco.Graphics;

/// <summary>
/// Device facts reported by the backend that cannot be requested: they follow
/// from the backend, the adapter and the library build, and are enabled
/// unconditionally whenever available. Query them with
/// <see cref="GPUDevice.IsCapabilitySupported"/>. Unlike
/// <see cref="GPUFeatures"/>, capabilities never appear in a device request.
/// </summary>
[Flags]
public enum GPUCapabilities : ulong
{
    None = 0,

    /// <summary>
    /// The device consumes shader modules through wgpu passthrough: submitted
    /// bytes reach the backend as-is (no Naga parsing). Required for
    /// DXIL/MSL/MetalLib (no translation fallback); SPIR-V passthroughs on
    /// Vulkan and translates through Naga otherwise.
    /// </summary>
    ShaderPassthrough = 1 << 0,

    /// <summary>
    /// Precompiled Metal libraries (AIR) are accepted as a passthrough source
    /// kind: the alco-gpu library (wgpu-core) was built with the metallib
    /// passthrough entry and the backend is Metal.
    /// </summary>
    MetalLib = 1 << 1,

    /// <summary>
    /// The native multi-draw-indirect path is available. The functionality
    /// itself needs no feature (wgpu 30 emulates multi-draw when the native
    /// path is absent); this capability marks the non-emulated path.
    /// </summary>
    MultiDrawIndirect = 1 << 2,

    /// <summary>
    /// Timestamp writes inside open render/compute passes use the native
    /// extension instead of pass-boundary emulation.
    /// </summary>
    TimestampInsidePasses = 1 << 3,
}
