# alco_gpu ABI Reference

`alco-gpu` is the engine's self-maintained Rust layer over **wgpu-core** (the same
architecture wgpu-native uses). It replaces wgpu-native and the hand-written native
Vulkan backend: Alco.Graphics P/Invokes a small, Alco-shaped C ABI, and every failure
surfaces as a catchable `GraphicsException` instead of a process-killing panic.

- Crate: `Src/Alco.Graphics.Native/alco-gpu` (cdylib `alco_gpu`, committed binaries in
  `Src/Alco.Graphics/runtimes/<RID>/native/`, provenance in `runtimes/alco-gpu-manifest.json`)
- C# side: `Src/Alco.Graphics/AlcoGpu/` (`Interop/AlcoGpuNative.cs` P/Invokes,
  `Interop/AlcoGpuStructs.cs` struct mirrors)
- Current ABI version: **1.2** (`ABI_MAJOR=1`, `ABI_MINOR=2`)

## Conventions

| Rule | Detail |
| --- | --- |
| Exports | `#[no_mangle] extern "C"`, `alco_` prefix, C symbol per function |
| Status | Every fallible export returns `AlcoStatus (u32)`; `0` = OK |
| Errors | Synchronous failures write a thread-local last-error (`alco_get_last_error`); C# throws at the call site |
| Async events | Device-lost / validation / native warnings queue per device; drained via `alco_device_pop_message` (severity 0 error, 1 warning, 2 info) |
| Handles | `AlcoHandle = u64` (`generation << 32 \| index`), one generational table per object type; stale / double-destroy returns `InvalidHandle`, never panics |
| Panics | Every export body is wrapped in `catch_unwind`; a panic becomes status `Panic` + message |
| Structs | `#[repr(C)]` ↔ `[StructLayout(LayoutKind.Sequential)]`, mirrored field-by-field in `AlcoGpuStructs.cs` |
| Bools | `u32` (`ALCO_TRUE = 1`) |
| Enums | `u32` with the same numeric values as the C# enums (identity cast — see `convert.rs`) |
| Sentinels | `ALCO_NONE = u32::MAX` (optional depth format, read-only load/store ops, timestamp index none, fragment output count = all writes); `u64::MAX` submit index in `alco_device_poll` = "latest" |
| Strings | In: NUL-terminated UTF-8; out: borrowed until the next call on that thread/device |

### AlcoStatus codes

`OK=0, InvalidHandle=1, InvalidArgument=2, Validation=3, OutOfMemory=4, DeviceLost=5, Panic=6, Unsupported=7, NotReady=8`.

`NotReady` is a control-flow value used by the map polling model (below), not an error.

## Export groups (86 exports)

**Meta** — `alco_abi_version`, `alco_build_info` (wgpu version, build id), `alco_get_last_error`.

**Device** — `alco_device_create` (backend request, debug flag, required `GPUFeatures`
bits, push-constants size; adapter selection is fully synchronous — no callbacks),
`alco_device_destroy`, `alco_device_get_info` (resolved backend, adapter name, supported
features, capability bits, limits incl. `max_bind_groups` / `max_immediate_size` /
`timestamp_period_ns`), `alco_device_poll`, `alco_device_pop_message`.

Requested features are intersected with adapter support inside `device_create`; the C#
side blanket-requests optional features and gates on the reported bits.

**Queue** — `alco_queue_write_buffer`, `alco_queue_write_texture` (all mips + optional
region), `alco_queue_submit` (consumes the command buffer handle, returns the submission
index used by blocking polls).

**Buffer** — create / destroy / `alco_buffer_map_read` / `alco_buffer_map_poll` /
`alco_buffer_get_mapped_range` / `alco_buffer_unmap`.

**Texture / View / Sampler** — create / destroy / `alco_texture_get_info` /
`alco_texture_create_view` (null descriptor = default view, used for surface textures) /
`alco_texture_release` (surface textures are released, never destroyed) / sampler pair.

**Shader modules** — create / destroy. Language enum covers WGSL / SPIR-V (4-byte
aligned) / DXIL / MSL / MetalLib; passthrough languages require the
`PassthroughShaders` capability, gated C#-side by `ShaderPassthroughEnabled`.

**Pipelines** — `alco_graphics_pipeline_create` (bind group layout handles, vertex
layouts, per-stage `{module, entry point}`, rasterizer/blend/depth-stencil value structs,
topology, color formats, optional depth format, fragment output count, push constants
size — the pipeline layout is built internally and is not visible to C#),
`alco_compute_pipeline_create`, `alco_pipeline_destroy`.

**Bind group layout (C# `GPUBindGroup`) and bind group (C# `GPUResourceGroup`)** —
create/destroy pairs. Layout entries carry binding/visibility/type plus a type-specific
payload; **bind group entries carry an explicit resource kind tag (0 buffer, 1 texture
view, 2 sampler)** — handles from different per-type tables can collide numerically, so
the kind must never be recovered by probing tables.

**Command encoding** — encoder create/finish/destroy (finish consumes the encoder on
both success and failure); render pass begin/end + setters (pipeline, bind group, vertex
/ index buffer, scissor, stencil reference, immediates, draw, draw indexed, indirect
variants, multi-draw indirect, write timestamp, execute bundles); compute pass begin/end
+ setters; copies (`buffer_to_buffer`, `buffer_to_texture`, `texture_to_buffer`,
`texture_to_texture`); `alco_resolve_query_set`.

**Render bundles** — bundle encoder create/destroy/finish + the subset of setters above.

**Query sets** — create / destroy (type is always timestamp).

**Surface** — create (platform tag: Win32 / MetalLayer / Wayland / Xcb / Xlib / Android,
field mapping mirrors `SurfaceHandle.cs`), `alco_surface_get_capabilities`
(formats[64] / present modes[8]), configure, `alco_surface_get_current_texture`
(returns status enum + texture handle), present, release current texture, destroy.

Acquired surface textures report the **configured** format/size — wgpu-core derives the
acquired texture's descriptor from the surface configuration, so the values recorded at
`alco_surface_configure` time are authoritative (and resize is signaled through the
acquire status, not through size drift).

## Map / readback model

There are no C callbacks. Buffer mapping is a poll-driven state machine:

1. `alco_buffer_map_read` initiates the map (after the copies touching the buffer have
   been submitted).
2. `alco_device_poll` drives the map callbacks to completion.
3. `alco_buffer_map_poll` returns `NotReady` while mapping, `OK` when mapped,
   `Validation` on failure.
4. `alco_buffer_get_mapped_range` yields the pointer; `alco_buffer_unmap` ends access.

`alco_device_poll` with `wait=ALCO_TRUE` blocks until the given submission index
(`u64::MAX` = latest). With `wait=ALCO_FALSE` it performs one non-blocking pump; this is
implemented as a **zero-timeout wait**, not `PollType::Poll` — on wgpu-core 30.0.1 the
`Poll` variant never observes fence completion on the Vulkan backend, so pending maps
would never resolve. A zero-timeout wait takes the same hal-wait + triage path as a
blocking poll and reports "still in flight" as status OK (`Timeout` is swallowed).

## Enum passthrough table

All enums cross the ABI as raw `u32` casts of the C# values; `convert.rs` is the single
mapping point on the Rust side. Notable value ranges: `PixelFormat` 1..95,
`VertexFormat` 0..30, `BindingType` 1..6 (UniformBuffer=1, StorageBuffer=2, Sampler=3,
Texture=4, StorageTexture=5, SamplerComparison=6), `ShaderStage` bits
(Vertex=1<<0, Fragment=1<<4, Compute=1<<5), `BufferUsage` bits (MapRead=1<<0 ...
Indirect=1<<8, QueryResolve=1<<9), `TextureUsage` bits (Read=1<<0, Write=1<<1,
TextureBinding=1<<2, StorageBinding=1<<3, ColorAttachment=1<<4, DepthAttachment=1<<5).

## Error containment

Double-destroy and use-after-destroy of any object return `InvalidHandle` (verified by
`DoubleDestroyReturnsInvalidHandleAndKeepsProcessAlive` / `...InsteadOfCrashing`); the
device remains usable afterwards. This is the core reason the layer exists: the same
scenario under wgpu-native aborted the process.

## Building / updating the binary

Locally (win-x64 host):

```bash
cd Src/Alco.Graphics.Native/alco-gpu
cargo build --release
cp target/release/alco_gpu.dll ../../Alco.Graphics/runtimes/win-x64/native/
```

All 8 RIDs (win x64/arm64, linux x64/arm64, osx x64/arm64, android x64/arm64) are built
by the manual-dispatch **Native alco-gpu** workflow (`.github/workflows/native-alco-gpu.yml`):
manylinux 2.28 containers (QEMU for arm64), Android NDK r29 (API 21), macOS deployment
target 10.15 with an `@rpath` install name. The workflow opens a PR that replaces the
runtimes binaries and refreshes `runtimes/alco-gpu-manifest.json` (per-RID sha256).
