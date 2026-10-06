# alco_gpu ABI Reference

`alco-gpu` is the engine's self-maintained Rust layer over **wgpu-core** (the same
architecture wgpu-native uses). It replaces wgpu-native and the hand-written native
Vulkan backend: Alco.Graphics P/Invokes a small, Alco-shaped C ABI. Reported native
failures surface as catchable `GraphicsException`s; invalid non-null pointers and
violations of the caller lifetime contract are unsupported, not recoverable errors.

- Crate: `Src/Alco.Graphics.Native/alco-gpu` (cdylib `alco_gpu`, committed binaries in
  `Src/Alco.Graphics/runtimes/<RID>/native/`, provenance in `runtimes/alco-gpu-manifest.json`)
- C# side: `Src/Alco.Graphics/AlcoGpu/` (`Interop/AlcoGpuNative.cs` P/Invokes + error
  callback registration, `Interop/AlcoGpuStructs.cs` struct mirrors,
  `Interop/AlcoGpuMarshal.cs` throwing error callback)
- Current ABI version: **2.3** (`ABI_MAJOR=2`, `ABI_MINOR=3`). ABI 1 generational
  handles and device-first object method signatures are not binary-compatible.

## Conventions

| Rule | Detail |
| --- | --- |
| Exports | `#[no_mangle] extern "C-unwind"`, `alco_` prefix, C symbol per function (identical symbol names to plain `C`; `C-unwind` defines foreign-exception unwinding through the frame, see *Error callback*) |
| Status | Every fallible export returns `AlcoStatus (u32)`; `0` = OK |
| Errors | Synchronous failures replace the thread-local latest failure (`alco_get_last_error`) **and** fire the registered error callback. Successful and `NotReady` calls leave that failure untouched (see *Error callback*) |
| Async events | Device-lost / validation / native warnings queue per device; drained via `alco_device_pop_message` (severity 0 error, 1 warning, 2 info) |
| Handles | Typed opaque native pointers; each C# `Alco*Handle` is a sequential readonly struct containing one `nint`. Null required handles return `InvalidHandle`; other pointer validity is a caller precondition |
| Panics | Every export body is wrapped in `catch_unwind`; a panic becomes status `Panic` + message |
| Structs | `#[repr(C)]` ↔ `[StructLayout(LayoutKind.Sequential)]`, mirrored field-by-field in `AlcoGpuStructs.cs` |
| Bools | `u32` (`ALCO_TRUE = 1`) |
| Enums | `u32` with the same numeric values as the C# enums (identity cast — see `convert.rs`) |
| Sentinels | `ALCO_NONE = u32::MAX` (optional depth format, read-only load/store ops, timestamp index none, fragment output count = all writes); `u64::MAX` submit index in `alco_device_poll` = "latest" |
| Strings | In: NUL-terminated UTF-8, borrowed for the call. Out: export-specific borrowed lifetimes; latest-error text lasts until the next failure on that thread, device info until public device destruction, message text until the next pop on that device, build info for process lifetime |

### AlcoStatus codes

`OK=0, InvalidHandle=1, InvalidArgument=2, Validation=3, OutOfMemory=4, DeviceLost=5, Panic=6, Unsupported=7, NotReady=8`.

`NotReady` is a control-flow value used by the map polling model (below), not an error.

## Error callback

`alco_set_error_callback(callback, userdata)` registers a process-wide callback
(null unregisters). This restores the wgpu-native-era error model: the C# host
registers a `[UnmanagedCallersOnly]` callback that throws `GraphicsException`, so
every failure unwinds out of the `alco_*` call as a managed exception at the exact
call site — no per-call status checks are needed in Alco.Graphics.

Contract:

- The callback fires **synchronously on the calling thread**, from `guard` in
  `entry.rs`, **strictly after `catch_unwind` has returned**. This ordering is the
  load-bearing safety property: a managed exception thrown inside the callback is a
  foreign unwind, and had it been caught by `catch_unwind` it would be swallowed
  into an opaque payload and resume at an unspecified point. Firing after the
  guard's match means the unwind passes only through frames with no pending
  destructors — the guard frame, the export frame, and the host stub.
- Exports are declared `extern "C-unwind"` so a foreign unwind passing through their
  frames is defined behavior under the Rust `C-unwind` ABI (symbol names are
  unchanged). The callback type itself is `extern "C-unwind" fn(u32, *const c_char, *mut c_void)`.
- Failures are every status except `OK` and `NOT_READY` (`NOT_READY` is control
  flow and must never fire). `message` is the thread-local latest failure (borrowed
  until the next failure on that thread). Every failure path must explicitly record
  a fresh diagnostic with `set_error`, `set_error_from`, or `fail`; `fail` supplies
  a generic default message. The guard does not inspect old TLS state or invent
  a fallback; returning a failure without recording its diagnostic violates this
  internal protocol. Rust panics record fresh text with status `Panic`.
- `OK` and `NOT_READY` do not clear, replace, format, or allocate TLS error text.
  `alco_get_last_error` observes the latest failure, not the status of the most
  recent call; it returns `OK` with null text if that thread has never failed.
  Copy the message when it must survive a later failure. Panic-hook installation
  uses one-time initialization with a read-only completed fast path.
- The callback **must not call back into the library**: a reentrant failure could
  replace the message while the callback still holds its pointer, and reentry
  could violate the exclusive borrow of the object being operated on.
- Unregistered (the default) behavior simply returns the failure status. The host
  may poll `alco_get_last_error`; registering the callback adds synchronous
  notification, not a different ownership or consumption contract.

Verification: `entry.rs` unit tests cover firing with status + message, the
default-message path, silence for `OK`/`NOT_READY`, and unregistration;
`AlcoGpuAbiTests`/`AlcoGpuRegressionTests` exercise null-handle failures, real-buffer
operation validation, retained latest-error text across success/`NOT_READY`,
validation root causes, and failed consuming operations. Raw double-destroy and
stale-pointer tests are deliberately excluded because those calls violate ABI 2.

## Log callback

wgpu-core reports internal diagnostics — root causes that never surface through
return values, such as the indirect-validation initialization failure that
surfaces as `DeviceError::Lost` — exclusively through the `log` crate. Without a
`log::Log` implementation installed, those records are dropped silently.
`alco_set_log_callback(callback, userdata)` (mirroring wgpu-native's
`wgpuSetLogCallback`/`wgpuSetLogLevel`) installs a process-wide forwarder that
delivers every record to the host:

- The callback is `(level, message, userdata)` with `log_level` values
  `OFF=0, ERROR=1, WARN=2, INFO=3, DEBUG=4, TRACE=5`; `message` is NUL-terminated
  UTF-8 **borrowed for the duration of the call only** — the host must copy.
- The forwarder installs lazily on the first registration; when no level was
  configured, it defaults to Warn. `alco_set_log_level(level)` adjusts the
  filter later (unknown values fail with `INVALID_ARGUMENT`).
- Unlike the error callback, the log callback is plain `extern "C"` and **must
  not throw**: records fire synchronously from deep inside wgpu-core stack
  frames that are not `C-unwind`, so a foreign unwind started there is
  undefined behavior. It also must not call back into the library.
- Registration fails with `Unsupported` when another library already owns the
  process-wide `log` logger (records would never reach the forwarder).

C# wiring: `AlcoGpuLogRouter` registers `AlcoGpuMarshal.OnNativeLog` when the
first `AlcoGpuDevice` is constructed — before native device creation, so
device-creation root causes are captured — and routes records to the device
host log (`Error` → `LogError`, `Warn` → `LogWarning` on debug devices,
`Info/Debug/Trace` → `LogInfo`). Error-severity records deliberately do not
throw (they can fire mid-native-operation; synchronous failures already throw
through the error callback).

Verification: `logging.rs` unit tests cover level+message delivery, filtering,
unregistration, idempotent registration and unknown-level rejection;
`AlcoGpuAbiTests` assert the `SetLogLevel`/`SetLogCallback` contracts.

## Export groups

**Meta** — `alco_abi_version`, `alco_build_info` (wgpu version, build id),
`alco_get_last_error`, `alco_set_error_callback`, `alco_set_log_callback`,
`alco_set_log_level`.

**Device** — `alco_device_create` (backend request, debug flag, required `GPUFeatures`
bits, push-constants size; adapter selection is fully synchronous — no callbacks),
`alco_device_destroy`, `alco_device_get_info` (resolved backend, adapter name, supported
features, capability bits, limits incl. `max_bind_groups` / `max_immediate_size` /
`timestamp_period_ns`), `alco_device_poll`, `alco_device_pop_message`.

Requested features are intersected with adapter support inside `device_create`; the C#
side blanket-requests optional features and gates on the reported bits.

**Queue** — `alco_queue_write_buffer`, `alco_queue_write_texture` (all mips + optional
region), `alco_queue_submit` (consumes the command buffer handle, returns the submission
index used by blocking polls), `alco_queue_submit_batch` (consumes an array of command
buffer handles as one submission in array order — the wgpu-native array submit shape;
count must be non-zero and the array non-null).

**Buffer** — create / destroy / `alco_buffer_map_read` / `alco_buffer_map_write`
(mapped range from `alco_buffer_get_mapped_range` is writable after a write map) /
`alco_buffer_map_poll` / `alco_buffer_get_mapped_range` / `alco_buffer_unmap`.

**Texture / View / Sampler** — create / destroy / `alco_texture_get_info` /
`alco_texture_create_view` (null descriptor = default view, used for surface textures) /
`alco_texture_release` (surface textures are released, never destroyed) / sampler pair.

**Shader modules** — create / destroy. Language enum covers WGSL / SPIR-V / DXIL /
MSL / MetalLib; passthrough languages require the `PassthroughShaders` capability,
gated C#-side by `ShaderPassthroughEnabled`. Synchronous creation borrows binary
payloads and valid UTF-8 text. Aligned little-endian SPIR-V borrows the input words;
unaligned sources are decoded once. The library is producer-agnostic: it performs no
Slang-specific SPIR-V post-processing (the managed compile pipeline normalizes
Slang's default-only switch wrappers before submission, see
`Alco.Graphics/Compiler/Spirv/SpirvNormalizer.cs`), and the trailing `flags` field
of `AlcoShaderModuleDesc` declares input properties — bit
`shader_module_flags::SPIRV_ADJUSTED_COORDINATES` skips Naga's GL-style Y
adjustment for SPIR-V that already matches its coordinate convention (Slang's
direct emission). Unknown flag bits are ignored.
Callers retain source storage until creation returns; no borrowed source escapes
the call.

**Pipelines** — `alco_graphics_pipeline_create` (bind group layout handles, vertex
layouts, per-stage `{module, entry point}`, rasterizer/blend/depth-stencil value structs,
topology, color formats, optional depth format, fragment output count, push constants
size — the pipeline layout is built internally and is not visible to C#),
`alco_compute_pipeline_create`, `alco_graphics_pipeline_destroy`, and
`alco_compute_pipeline_destroy`. Graphics and compute pipeline pointers are
separate types; the shared ABI 1 `alco_pipeline_destroy` export is removed.

**Bind group layout (C# `GPUBindGroup`) and bind group (C# `GPUResourceGroup`)** —
create/destroy pairs. Layout entries carry binding/visibility/type plus a type-specific
payload; **bind group entries carry an explicit resource kind tag (0 buffer, 1 texture
view, 2 sampler)** alongside a pointer-sized heterogeneous `resource` (`nint` in
C#). The kind declares the exact pointee type; there is no runtime type-probing or
wrong-kind recovery. All homogeneous descriptor fields and handle arrays use the
appropriate typed handle.

**Command encoding** — encoder create/finish/destroy (finish consumes the encoder on
both success and failure); render pass begin/end/release + setters (pipeline, bind group, vertex
/ index buffer, scissor, **viewport**, **blend constant**, stencil reference, immediates, draw,
draw indexed, indirect variants, the full multi-draw family (indirect, indirect count, indexed
variants), **debug markers / debug groups**, write timestamp, execute bundles); compute pass
begin/end/release + setters including **debug markers / debug groups**; copies
(`buffer_to_buffer`, `buffer_to_texture`, `texture_to_buffer`, `texture_to_texture`);
`alco_encoder_clear_buffer` (size zero = to the end) and `alco_encoder_clear_texture`
(`AlcoSubresourceRange`, counts zero/`ALCO_NONE` = the rest; needs the `ClearTexture`
feature); `alco_resolve_query_set`; encoder-level **debug markers / debug groups**.

**Render bundles** — bundle encoder create/destroy/finish + the subset of setters above,
including debug markers / debug groups recorded into the bundle.

**Query sets** — create / destroy (type is always timestamp).

**Surface** — create (platform tag: Win32 / MetalLayer / Wayland / Xcb / Xlib / Android,
field mapping mirrors `SurfaceHandle.cs`), `alco_surface_get_capabilities`
(formats[64] / present modes[8]), configure, `alco_surface_get_current_texture`
(returns status enum + texture handle), present, release current texture, destroy.

Acquired surface textures report the **configured** format/size — wgpu-core derives the
acquired texture's descriptor from the surface configuration, so the values recorded at
`alco_surface_configure` time are authoritative (and resize is signaled through the
acquire status, not through size drift).

## Typed pointers and call context

The ABI has no Alco handle registry, generation counter, slot lookup, address
quarantine, or runtime type discovery. Each non-null handle points to one
independently owned native wrapper. C# mirrors are `AlcoDeviceHandle`,
`AlcoBufferHandle`, `AlcoTextureHandle`, `AlcoTextureViewHandle`, `AlcoSamplerHandle`,
`AlcoShaderModuleHandle`, `AlcoBindGroupLayoutHandle`, `AlcoBindGroupHandle`,
`AlcoQuerySetHandle`, `AlcoGraphicsPipelineHandle`, `AlcoComputePipelineHandle`,
`AlcoEncoderHandle`, `AlcoCommandBufferHandle`, `AlcoRenderPassHandle`,
`AlcoComputePassHandle`, `AlcoBundleEncoderHandle`, `AlcoRenderBundleHandle`, and
`AlcoSurfaceHandle`. Each contains exactly one native-sized `nint`; copying the
value copies a borrowed identity, not ownership or a reference count.

- Device creation has no device parameter. Other top-level object creation takes
  a live typed device pointer. Texture-view creation is texture-only: it derives
  its context from the texture.
- Queue writes and submission still take the live device plus the typed target
  buffer/texture/command buffer. These supplied objects must belong to that device.
- All object methods and destruction/release derive the context from their object;
  they omit the redundant device parameter. Copies and query resolution are
  encoder-first. Pipeline destruction is split by graphics/compute pointer type.
- Render/compute pass begin is encoder-only. Pass end/release, setters, and
  draw/dispatch are pass-only. Bundle recording is bundle-encoder-only.
- Every supplied non-null pointer must be live, correctly aligned, of the exact
  declared type, and from the same device context as the controlling object.
  Heterogeneous bindings require the corresponding `resource` pointer and `kind`.
  These are caller preconditions, not dynamic validation promises.

Null required handles are recoverable `InvalidHandle` failures. A null optional
handle retains its export-specific meaning (for example, no resolve view). A
non-null freed, fabricated, incorrectly typed, or concurrently released pointer is
unsupported and can cause undefined behavior; `catch_unwind` cannot make it safe.
Address reuse after release is unconstrained. Only simultaneously live wrapper
identities are distinct; callers must not interpret pointer bits as indices or
assert reuse/non-reuse of released addresses.

## Threading and ownership

Ordinary ABI operations borrow the live wrapper and its retained cleanup context
directly. They do not clone an `Arc<DeviceCtx>` or acquire an Alco registry lock to
resolve each argument. Creation establishes each child's retained context owner;
destruction/consumption drops it. Surface ownership and genuine asynchronous map
completion use their required shared ownership, not a per-call device pin.
wgpu-core still has its own resource registries and internal synchronization.

Different command buffers may be recorded in parallel without a device-wide
recording lock. Independent resources may be created/destroyed concurrently.
These guarantees require callers to keep the public device and supplied resources
alive until each operation completes, including callbacks and GPU waits. Serialize
use, end/finish, submission, and destroy/release of the same mutable object;
order resource destruction against every call borrowing that resource. Device
teardown must not overlap active operations. A retained cleanup context does not
permit normal GPU work after public device invalidation.

Surface configuration/acquisition/presentation state retains a per-surface mutex.
Map callbacks retain completion state independently of the buffer wrapper, publish
completion atomically, and protect error text with its completion-local mutex.
These real async/state locks remain necessary; they are not handle-validation
locks. The independent-resource/recording contract is not a blanket thread-safety
guarantee for queue operations (including initial-data uploads), readbacks,
mapping/unmapping, polling, or surfaces. Follow operation-specific synchronization.
Mapped pointers must not outlive unmap or buffer release; borrowed strings retain
the lifetimes documented by their exports. Acquired surface textures and their
views retain the parent surface independently of its public handle; the platform
window/display/layer must outlive the last retained child owner, not just
`alco_surface_destroy`. Managed `GC.KeepAlive` guards protect call-scoped wrapper
lifetimes, not synchronization against explicit disposal.

### Consumption and device-first cleanup

Destroy/release transfers the unique owner back to native code exactly once. For
a valid input, encoder finish, bundle encoder finish, pass end, and queue submit
consume their input on success **and on validation/core failure**; the error
callback fires only after that cleanup. Hosts must clear owned mirrors before
calling a consuming export, even if the call later throws. Missing required
arguments are precondition failures, not a promise that other inputs were consumed.

`alco_render_pass_release` and `alco_compute_pass_release` abandon a live pass
without calling End or reporting deferred pass validation. They consume the pass
wrapper and allow cleanup of open recordings, including after device invalidation.
Release is not a way to recover and continue a valid recording; abandon its parent
encoder as well. End is the normal recording path; its recorded validation may be
reported only by encoder finish.

Public `alco_device_destroy` invalidates and releases the unique device owner and
shuts down normal device work. It does **not** bulk-free independently owned child
wrappers. Each child retains the context needed for later cleanup. Final core
queue/device/adapter registrations and the context are reclaimed after the last
retained child owner is released. A destroyed device pointer must never be used
again, even while children keep that internal context alive.

The .NET layer separates public device liveness from native child ownership.
`Dispose` may defer release while the device is active; shutdown drains owned
staging/default resources and invalidates the public device. Late explicit disposal
or finalization still releases each child's pointer, rather than skipping native
cleanup because the public device is dead. Late open passes are abandoned with
Release. `BaseGPUObject` ownership makes repeated managed `Dispose`/`Destroy`
idempotent and clears consumed pointers before native calls; raw repeated native
destruction is **not** idempotent. Framebuffers also release their nested texture
and view owners. No production pointer registry or counter ABI is needed for this
ownership model.

**wgpu-core 30.0.1 still has a texture-upload/submission lock-order inversion.**
`Queue::write_texture` holds the texture initialization write lock while acquiring
`device.trackers`; `Queue::submit` holds `device.trackers` while initializing the
same texture. Full writes are also affected when initialization actions were
recorded before the upload. Alco.Graphics retains `_textureUploadLock` per device
around texture writes and every queue submission, including readbacks. It does not
cover recording, buffer writes, mapping, polling, or GPU waits. Direct C ABI users
must provide the same synchronization when racing texture writes with submissions.

## Map / readback model

Buffer mapping remains poll-driven (the error callback above is only for failures;
async completion is still polled, and async paths never throw from a callback — the
old wgpu-native layer followed the same rule):

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

## Error containment and verification

Recoverable argument errors, null required handles, core validation failures,
unsupported operations, and caught Rust panics use the status/TLS/error-callback
contract. Full error source chains retain their validation root causes. Raw stale
pointer access, double-free, fabricated pointers, wrong pointee types, and invalid
device-context combinations are caller contract violations; they are not tested
by executing unsafe calls and carry no recoverable-error guarantee.

Managed coverage verifies all typed pointer layouts and homogeneous descriptor
fields, simultaneous live identities, many independent allocation/destruction
rounds with real buffer copies, render pixels, validation chains, and independent
resource/recording outputs. Texture upload/submission tests retain the managed
upload gate. Failed encoder/bundle finishes and a submission referencing a
destroyed **core** resource verify consumption without dereferencing a freed
wrapper. Device-first tests verify late owned-pointer clearing, open recording
abandonment, idempotent managed disposal, nested attachment cleanup, staging cleanup,
and silent orphan finalization. Existing Debug allocation accounting checks
managed unmanaged-temporary balance; it is not a count of Rust wrappers.

Native tests verify deeper wrapper/context reclamation using existing core reports,
weak context ownership, and reference-count checks. These test-only observations
do not introduce a production registry or live-counter export. ABI 2 tests must
run against a freshly built ABI 2 library, never an ABI 1 delivered binary.

## Building / updating the binary

### Local builds and prerequisites

`Alco.Graphics.csproj` ensures that the **selected** `RuntimeIdentifier` has a native
library during build, publish, and NuGet pack. A current delivered library in
`Src/Alco.Graphics/runtimes/<RID>/native/` is used without invoking Cargo or requiring
Rust. Its adjacent `<library>.source.sha256` sidecar must match both the current source
fingerprint and the library's SHA-256. Missing, unstamped, or stale deliveries select a
local source build (with a diagnostic), never silently run or package an old artifact.
MSBuild automatically runs `cargo build --locked --release
--target <triple>` in `Src/Alco.Graphics.Native/alco-gpu`, honoring its pinned
`rust-toolchain.toml` (Rust **1.97.1**) and `Cargo.lock`. It does not install tools,
download replacement binaries, update the manifest, or write to the source runtimes
directory. `RUSTUP_AUTO_INSTALL=0` prevents implicit toolchain installation. Cargo may
download locked crate dependencies on the first build; offline builds need those crates
cached in advance.

Install Rust/Cargo with rustup and the selected target **before** building a missing
RID, for example `rustup target add --toolchain 1.97.1 aarch64-pc-windows-msvc`.
Installing the Rust target is not enough: its native linker and SDK/sysroot must also
be available. A missing Cargo executable, target, linker, or failed native compilation
produces an actionable MSBuild error rather than silently excluding the library.

| RID | Rust target | Library | Required native tools |
| --- | --- | --- | --- |
| `win-x64` | `x86_64-pc-windows-msvc` | `alco_gpu.dll` | Visual Studio C++ Build Tools (x64) and Windows SDK |
| `win-arm64` | `aarch64-pc-windows-msvc` | `alco_gpu.dll` | Visual Studio C++ ARM64 build tools/libraries and Windows SDK ARM64 libraries |
| `linux-x64` | `x86_64-unknown-linux-gnu` | `libalco_gpu.so` | C linker and glibc development sysroot (for example `build-essential`, `pkg-config` on Ubuntu) |
| `linux-arm64` | `aarch64-unknown-linux-gnu` | `libalco_gpu.so` | Native ARM64 C toolchain, or `aarch64-linux-gnu-gcc` and matching sysroot |
| `osx-x64` | `x86_64-apple-darwin` | `libalco_gpu.dylib` | Xcode command-line tools and macOS SDK |
| `osx-arm64` | `aarch64-apple-darwin` | `libalco_gpu.dylib` | Xcode command-line tools and macOS SDK |
| `android-x64` | `x86_64-linux-android` | `libalco_gpu.so` | Android NDK r29, `x86_64-linux-android21-clang` linker |
| `android-arm64` | `aarch64-linux-android` | `libalco_gpu.so` | Android NDK r29, `aarch64-linux-android21-clang` linker |

Cross-compilation is not automatically provisioned. Build on a supported host or
configure Cargo's target-specific linker environment, for example
`CARGO_TARGET_AARCH64_UNKNOWN_LINUX_GNU_LINKER=aarch64-linux-gnu-gcc` or
`CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER=<NDK>/toolchains/llvm/prebuilt/<host>/bin/aarch64-linux-android21-clang`
(use `.cmd` on Windows). Android x64 uses `CARGO_TARGET_X86_64_LINUX_ANDROID_LINKER`.
For Windows cross-builds, run in the appropriate VS developer environment if needed;
MSVC must have the destination architecture's libraries, not just an x64 linker.
Local macOS builds default to deployment target 10.15 (override with
`MACOSX_DEPLOYMENT_TARGET`) and an `@rpath/libalco_gpu.dylib` install name. Local Linux
builds use the host glibc; use the manual workflow's manylinux recipe for distributable
binaries with the documented glibc 2.28 baseline.

Pass `-r <RID>` explicitly when the repository's OS defaults do not match the desired
architecture (the defaults are Windows/Linux x64 and macOS ARM64). For example:

```bash
dotnet build Src/Alco.Graphics/Alco.Graphics.csproj -r win-arm64
dotnet publish Sandbox/0-BasicWindow/0-BasicWindow.csproj -r linux-x64
dotnet pack Src/Alco.Graphics/Alco.Graphics.csproj -r linux-x64
```

The generated file lives under
`Src/Alco.Graphics/obj/alco-gpu/<triple>/release/` and is dynamically added as Content
**after** Cargo runs. It is copied to the application/output and publish directories
with its platform file name so the native loader can find it, including through project
references. NuGet pack places the selected library at `runtimes/<RID>/native/<library>`;
packing one RID does not imply that binaries for the other seven RIDs are included.
The same checks apply to `publish --no-build` and `pack --no-build`.

Local generated libraries track Rust source, Cargo manifest/lockfile, toolchain, and
project timestamps via MSBuild `Inputs`/`Outputs`; unchanged builds skip Cargo.
Delivered binaries deliberately do **not** track checkout timestamps: validated source
and binary fingerprints allow a fresh checkout to build without Rust. Generated
libraries also validate their sidecars so source deletions or edits preserving file
timestamps cannot bypass rebuilding. NuGet consumers receive ordinary native runtime
assets only; the source-build target and its Rust prerequisite are not packaged.
When editing Rust or validating native changes, use
`-p:AlcoGpuBuildNative=true` to select the source-built library even if a delivered one
exists. This retains incremental compilation and never replaces the delivered binary.
Use `-p:AlcoGpuCargoTargetDirectory=<absolute-path>` for an isolated Cargo cache and
`-p:AlcoGpuCargoExecutable=<path-to-cargo>` if Cargo is not on PATH. After changing
linker flags/environment, clear that native cache (or use a different directory) to
force rebuilding. `dotnet msbuild Src/Alco.Graphics/Alco.Graphics.csproj
-t:ResolveAlcoGpuNative -p:RuntimeIdentifier=<RID> -v:normal` inspects the mapping
without running Cargo or copying files.

### CI and delivered binaries

The regular **Build** workflow (`.github/workflows/build.yml`) installs pinned Rust
and compiles the selected host RID through the same MSBuild integration with
`AlcoGpuBuildNative=true`, then runs the full normal test suite against those actual
outputs. Linux installs Mesa's lavapipe Vulkan software ICD (`mesa-vulkan-drivers`,
`libvulkan1`) in addition to build tools; macOS uses Metal and Windows uses its installed
adapter. These are runtime GPU prerequisites, distinct from Rust/linker prerequisites.

All 8 RIDs are also built by the manual-dispatch **Native alco-gpu** workflow
(`.github/workflows/native-alco-gpu.yml`), using pinned Rust and `--locked --release`:
manylinux 2.28 containers (QEMU for arm64), Android NDK r29 (API 21), macOS deployment
target 10.15 with an `@rpath` install name. Its optional PR replaces delivered runtimes
binaries and their `.source.sha256` sidecars, and refreshes
`runtimes/alco-gpu-manifest.json` (per-RID binary/source SHA-256 provenance).
Local builds leave delivered artifacts and the manifest untouched.

A delivery sidecar has exactly two lines: the lowercase source fingerprint, then the
lowercase SHA-256 of the binary. The source fingerprint is SHA-256 over sorted,
crate-relative POSIX paths for `Cargo.toml`, `Cargo.lock`, `rust-toolchain.toml`, all
`src/**/*.rs`, and optional `build.rs` / `.cargo/config.toml`. Each entry is UTF-8
`<path> + NUL + <lowercase SHA256 of UTF-8 source text> + LF`; source text strips a
UTF-8 BOM and normalizes CRLF/lone CR to LF. This avoids checkout line-ending drift.
After rebuilding a delivered binary, obtain its source fingerprint with
`dotnet msbuild Src/Alco.Graphics/Alco.Graphics.csproj -t:ResolveAlcoGpuNative
-p:RuntimeIdentifier=<RID> -nologo -getProperty:_AlcoGpuSourceHash` and write both hashes
as UTF-8 lines to the adjacent sidecar. Do not stamp an old binary with a new source
fingerprint; it must actually have been rebuilt from those sources.
