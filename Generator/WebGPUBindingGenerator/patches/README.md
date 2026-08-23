# wgpu-native patches

Alco's renderer submits Slang-generated shaders through wgpu-native's shader
passthrough, which bypasses Naga entirely and hands the bytecode to the
platform backend:

- **Vulkan** consumes Slang SPIR-V through `wgpuDeviceCreateShaderModuleSpirV`
  (upstream entry point).
- **D3D12** consumes Slang DXIL containers through
  `wgpuDeviceCreateShaderModuleDxil`, and **Metal** consumes Slang MSL source
  through `wgpuDeviceCreateShaderModuleMsl` — both added by the second patch
  below, mirroring wgpu-core's generic
  `CreateShaderModuleDescriptorPassthrough { dxil / msl / .. }`.

Passthrough requires wgpu-core's `PASSTHROUGH_SHADERS` device feature, which
wgpu-native v29.0.1.1 does not map through its C API. The first patch exposes
and maps it.

Patches (apply in order to the exact `wgpu-native` tag `v29.0.1.1`):

1. `wgpu-native-v29.0.1.1-passthrough.patch` — exposes the
   `WGPUNativeFeature_PassthroughShaders` feature mapping (ffi/wgpu.h +
   src/conv.rs). It does not change shader bytecode or disable backend
   validation.
2. `wgpu-native-v29.0.1.1-dxil-msl-passthrough-abi.patch` — adds
   `WGPUShaderModuleDescriptorDxil/Msl` and
   `wgpuDeviceCreateShaderModuleDxil/Msl` (ffi/wgpu.h + src/lib.rs), each
   carrying the compute workgroup size Metal/DX12 cannot reflect from
   passthrough code.

## Building the binaries

The canonical build pipeline lives in the
[`alco-wgpu-native`](https://github.com/IssacZhuang/alco-wgpu-native) overlay
repository: it pins the upstream commit, applies this patch stack with SHA-256
verification and produces all eight Alco runtime identifiers via GitHub
Actions. This folder keeps copies of the same patches next to the C# binding
generator that consumes their header additions.

Locally (`win-x64` with the pinned Rust toolchain installed):

```powershell
python ./scripts/pipeline.py validate-config
python ./scripts/pipeline.py build --rid win-x64
```

Only backends whose adapter exposes `PassthroughShaders` take the passthrough
path; every other combination fails with an explicit `GraphicsException`
instead of an entry-point lookup error.
