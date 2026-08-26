# Material System Refactor Plan: Frequency-Based Bind Groups

## 1. Background

The material system currently maps **one shader "slot" to exactly one wgpu bind group**:

- `SLOT(set, bind)` in `Src/Alco.Engine/Assets/Shaders/Libs/Core.hlsli` expands to
  `[[vk::binding(bind, set)]]`; every `DEFINE_*` macro sends its first argument to the
  SPIR-V **set** index and fixes the binding to 0 (plus binding 1 for the paired sampler).
- On the C# side this assumption is baked into three places:
  - `ShaderReflection.BuildResourceIndex` (`Src/Alco.Graphics/Compiler/ShaderReflection.cs:133`)
    maps only `Bindings[0].Entry.Name` of each group to a resource id == group index.
  - `MaterialUtility` (`Src/Alco.Rendering/Material/MaterialUtility.cs`) recognizes only six
    group shapes of 1–2 bindings.
  - `ShaderParameterSet` (`Src/Alco.Rendering/Material/ShaderParameterSet.cs:900`) sizes its
    slot arrays to `BindGroups.Count` and stores one pre-baked single-resource
    `GPUResourceGroup` per group.

wgpu native allows at most **8 bind groups per pipeline layout**. Five shaders already sit at
that ceiling:

| Sets used | Shader |
|---|---|
| 8 | `PBR/DeferredLighting.hlsl` |
| 8 | `PBR/VoxelTrace.hlsl`, `PBR/VoxelPropagate.hlsl`, `PBR/VoxelInject.hlsl`, `PBR/VoxelDemosaic.hlsl` |

Meanwhile the actual per-stage budgets are far from exhausted: wgpu guarantees
16 sampled textures / 16 samplers / 12 uniform buffers / 8 storage buffers / 4 storage
textures **per shader stage**, and 1000 bindings per bind group. DeferredLighting needs
7 textures + 6 samplers + 1 uniform buffer = 14 bindings — that fits into a single group.

The industry-standard fix (CRYENGINE, Godot, Bevy, O3DE, The Forge, WebGPU best practices):
**bind groups are organized by update frequency, not by resource** — one group holds many
resources, distinguished by binding index. See the research notes in the discussion that
produced this plan (CRYENGINE 5.7.1 `EResourceLayoutSlot`, Godot renderer_rd sets 0–3,
Bevy groups 0–3, toji.dev bind group best practices).

## 2. Technical verification (PoC) — done

A scratch harness under `artifacts/poc-bindgroup/` (console project referencing
`Src/Alco.Rendering`) drove the engine's real compile→reflect→create path on two packed
shaders (`PackedLighting.hlsl`, `PackedLighting2.hlsl`). Results:

- **DXC compiles packed groups** without complaints (vs/ps_6_0, `-spirv -O3`,
  `-fspv-preserve-interface -fspv-preserve-bindings`).
- **Engine reflection is already generic over multi-binding groups**:
  `ShaderReflectionUtility` enumerates every binding of every set with correct name, type,
  binding index and stage flags; `BindGroupLayout.ToDescriptor()` converts an N-entry group.
- **`ValidateBindGroupLayouts` passes unchanged** — it only checks group count ≤ max and
  contiguity from 0; it is indifferent to bindings per group.
- **A real headless `WebGPUDevice` (wgpu Vulkan backend) accepts** a 14-entry bind group
  layout (1 uniform buffer + 7 sampled textures + 6 samplers, fragment stage) and a bind
  group populated with real resources — no validation error. The reflection-driven variant
  (`BindGroups[1].ToDescriptor()`, 13 entries) also works end-to-end.
- Confirmed breakage points are exactly the three listed in §1 (name→id map, MaterialUtility
  shapes, ShaderParameterSet slot model). The GPU abstraction
  (`ResourceGroupDescriptor.Resources[]`, `WebGPUBindGroup`, `WebGPUResourceGroup`,
  `GPUDevice.CreateResourceGroup`, `RenderPass.SetResources(slot, group)`) already supports
  N-entry groups unchanged.

Re-run: `cd artifacts/poc-bindgroup && dotnet build && ../../Bin/Debug/PoCBindGroup/win-x64/PoCBindGroup.exe`
(full log: `artifacts/poc-bindgroup/poc-output.log`).

## 3. Target design

### 3.1 Frequency group convention (shader-visible contract)

Four groups, ordered least→most frequently updated (matches CRYENGINE/Godot and stays within
the 4-group floor guaranteed by the WebGPU spec, keeping a future Dawn/web target possible):

| Set | Name | Contents |
|---|---|---|
| 0 | `ALCO_GROUP_FRAME` | Per-frame/per-view constants: camera, environment, time, future shared sampler bank |
| 1 | `ALCO_GROUP_PASS` | Per-pass inputs: G-buffer, shadow maps, GI atlas, pass constants |
| 2 | `ALCO_GROUP_MATERIAL` | Material CB + material textures |
| 3 | `ALCO_GROUP_DRAW` | Per-draw/instance data (push constants remain the lightweight alternative) |

Only groups 0 and 1 are exercised in this refactor; 2 and 3 are reserved for the forward
path and instancing later.

### 3.2 Shader layer (`Core.hlsli`)

Add explicit (set, binding) macro variants and redefine the existing macros as wrappers, so
**all 49 existing shaders produce byte-identical SPIR-V without being touched**:

```hlsl
#define ALCO_GROUP_FRAME    0
#define ALCO_GROUP_PASS     1
#define ALCO_GROUP_MATERIAL 2
#define ALCO_GROUP_DRAW     3

// Explicit variants: resource lives at (set, bind); the paired sampler takes bind + 1.
#define DEFINE_UNIFORM_AT(set, bind, name)        SLOT(set, bind) cbuffer name
#define DEFINE_TEX2D_SAMPLE_AT(set, bind, name)   SLOT(set, bind) Texture2D name; SLOT(set, bind + 1) SamplerState name##Sampler
// ... same pattern for TEX2D_READ / TEX2D_STORAGE / TEX3D_* / TEX2D_DEPTH / TEX2D_DEPTH_SAMPLE / STORAGE ...

// Legacy macros keep working with identical layout (slot = set, implicit binding 0/1):
#define DEFINE_TEX2D_SAMPLE(index, name)          DEFINE_TEX2D_SAMPLE_AT(index, 0, name)
// ... etc for all 10 resource macros
```

Rules that must be preserved (consumed by name-based post-processing):

- Sampler naming: texture `_x` pairs with `_xSampler` (used by `SAMPLE_TEX2D` and by
  `ShaderUtility.MarkDepthComparisonSamplers`, `Src/Alco.Rendering/Shader/ShaderUtility.cs:358`).
- Depth macros: `SpirvDepthTexturePatcher` finds depth texture names by regexing the shader
  text for the `DEFINE_TEX2D_DEPTH*` macro calls (`ShaderUtility.cs:20-25`). The regexes must
  be extended to also match the `_AT` forms (capture group = texture name).

### 3.3 DeferredLighting.hlsl regrouping (8 sets → 2)

```hlsl
DEFINE_UNIFORM_AT(ALCO_GROUP_FRAME, 0, _data) { ... };

DEFINE_TEX2D_SAMPLE_AT(ALCO_GROUP_PASS, 1, _albedo);        // b1  tex, b2  sampler
DEFINE_TEX2D_SAMPLE_AT(ALCO_GROUP_PASS, 3, _normal);        // b3  tex, b4  sampler
DEFINE_TEX2D_SAMPLE_AT(ALCO_GROUP_PASS, 5, _mrAO);          // b5  tex, b6  sampler
DEFINE_TEX2D_DEPTH_AT(ALCO_GROUP_PASS, 7, _gbufferDepth);   // b7  tex (read)
DEFINE_TEX2D_DEPTH_SAMPLE_AT(ALCO_GROUP_PASS, 8, _shadowMap); // b8 tex, b9 cmp sampler
DEFINE_TEX2D_SAMPLE_AT(ALCO_GROUP_PASS, 10, _emissive);     // b10 tex, b11 sampler
DEFINE_TEX2D_SAMPLE_AT(ALCO_GROUP_PASS, 12, _indirectGI);   // b12 tex, b13 sampler
```

Fragment-stage budget: 7 sampled textures ≤ 16, 6 samplers ≤ 16, 1 UBO ≤ 12. Vertex stage
uses only the UBO.

### 3.4 Voxel shader regrouping (8 sets → 2–3, compute)

`VoxelCommon.hlsli` owns set 0 (`DEFINE_UNIFORM(0, _data)`); each pass adds sets 1–7.
Regroup by direction of data flow:

- set 0: uniform buffers + read-only inputs (sampled textures 3D, read-only storage buffers)
- set 1: write outputs (storage textures ≤ 4 per stage, storage buffers ≤ 8 per stage)
- set 2 (only where set 1 overflows): additional outputs/inputs

Exact binding maps are produced per shader at implementation time against the budget table
above (`WGPULimits` defaults in `Src/Alco.Graphics/WGPU/Bindings/WGPULimits.cs`); the
`-fspv-preserve-bindings` flag means **unused-but-declared resources still count toward the
budget**, so declarations must not be padded.

### 3.5 Reflection layer (`ShaderReflection`)

Replace the "first binding name per group" index with a full resource map:

```csharp
// New: one entry per settable resource (uniform/storage buffers, textures of all kinds).
// Samplers and storage-counter bindings are excluded — they are implied by their parent resource.
public readonly struct ShaderResourceLocation
{
    public readonly int GroupIndex;    // index into BindGroups
    public readonly uint Binding;      // binding index inside the group
    public readonly int EntryIndex;    // index into BindGroups[GroupIndex].Bindings
    public readonly BindingType Type;  // from the reflected entry
}
```

- `BuildResourceIndex`: iterate **all** bindings of all groups; register every settable
  resource name → `ShaderResourceLocation`; skip `Sampler`/`SamplerComparison` entries and
  counter bindings (identified by the existing `IsStorageBufferWithCounterGroup` pairing or
  by adjacency convention).
- Resource ids: dense ordinals `0 .. ResourceCount-1` ordered by (group, binding). id is an
  **opaque handle** (Unity `Shader.PropertyToID` analogue) — no longer a group index.
- `TryGetResourceId(name, out uint id)` / `GetResourceName(uint id)` keep their signatures;
  internally they go through the new map.
- New accessor: `TryGetLocation(string name, out ShaderResourceLocation)` and
  `GetLocation(uint id)`.
- `ValidateBindGroupLayouts` unchanged.

### 3.6 Material layer (`ShaderParameterSet` v2)

Internal model changes from "one slot per group" to "one slot per settable resource; one
assembled bind group per reflection group":

- `_slots`: sized to `ResourceCount`. Each slot keeps the resource reference(s) it owns:
  buffer / texture / renderTexture (+ renderTextureIndex, + mip level for the 3D storage/read
  setters), exactly the fields it has today, plus the resolved bindable views created at Set
  time (same places where today a pre-baked `GPUResourceGroup` was fetched: `EntryReadonly`,
  `EntrySample`, `EntryDepthComparison`, mip views, ...).
- `_groups`: sized to `BindGroups.Count`; each entry holds the assembled
  `GPUResourceGroup?` + a dirty flag.
- Assembly (on demand, per dirty group): walk the group's reflection `Bindings`; for each
  entry pull the matching bindable from the owning slot and emit a
  `ResourceBindingEntry { Binding = entry.Binding, Resource = ... }`:
  - `UniformBuffer`/`StorageBuffer` → the slot's `GPUBuffer` (with-counter slots also feed
    the adjacent counter binding, today's `EntryReadWriteWithCounter` behavior).
  - `Texture` → the slot's view: color view / storage view / mip-rebased view / depth view,
    chosen exactly as today by slot type + texture sample type (depth).
  - `Sampler`/`SamplerComparison` → the device default sampler or default comparison
    sampler, matching today's `EntrySample`/`EntryDepthComparison` pairing rules.
  Then `GPUDevice.CreateResourceGroup(new ResourceGroupDescriptor { Layout = groupBindGroup,
  Resources = entries })`. The group's `GPUBindGroup` layout comes from
  `BindGroupLayout.ToDescriptor()` → `GPUDevice.CreateBindGroup`, **cached per
  ShaderReflection** (shared by all materials using the same shader).
- Public API — **unchanged signatures, unchanged behavior from the caller's view**:
  - `SetBuffer/SetTexture/SetRenderTexture/SetRenderTextureDepth/SetTexture3DStorage/
    SetTexture3DRead` (+ `Try*` variants, name and id overloads) resolve name→location (or
    id→location), validate the slot type, store the resource, mark the owning group dirty.
  - Getters read the slot as today.
  - `ResourceGroups` span remains per-group; instead of pre-baked single-resource groups it
    now returns assembled groups (rebuilding dirty ones first — moved into `PushResources`).
- `MaterialUtility`: the six shape classifiers remain for the *entry-level* type checks
  (depth detection, white-texture defaulting) but stop being used to type whole groups;
  `ShaderParameterSet` types slots from the reflected entry type directly.
- `GraphicsMaterial.UpdateSlotResources` white-texture defaulting
  (`GraphicsMaterial.cs:50-77`) generalizes from "group is TextureSampler" to "entry is a
  non-depth sampled texture with nothing bound" (bind `TextureWhite` + default sampler).
- `GraphicsMaterial.PushResources` (`GraphicsMaterial.cs:20-33`): rebuild dirty groups, then
  `SetResources(groupIndex, group)` as today. Compute path (`ComputeMaterial`) goes through
  the same parameter set and needs no structural change.

Bind groups are immutable, so a naive implementation pays one `wgpuDeviceCreateBindGroup`
per owning group per change — which the audit in §4 shows would be 10²–10³ creations per
frame. Two mandatory mechanisms bring the steady state to ≈ 0:

- **Identity no-op check**: every `Set*`/`TrySet*` first compares the incoming resource
  against the slot's current one (reference equality of buffer/texture/renderTexture +
  same renderTextureIndex + same mipLevel); if identical, return success without dirtying
  the group. This makes the large class of unconditional per-frame setters free. In-repo
  precedent: `VoxelGiRenderer.cs:880-892` and `HbaoRenderer.cs:105-114` already hand-roll
  exactly this guard — the refactor moves it into the parameter set itself.
- **Content-keyed bind group cache**: per group slot, a `Dictionary<ulong, GPUResourceGroup>`
  (bounded at 16 entries, FIFO eviction) keyed by a hash of the assembled content — the
  ordered (binding, resource-identity) pairs, where resource identity is the managed
  reference of the bindable buffer/view. Assembly path: dirty group → compute content hash
  → cache hit: reuse; miss: `CreateResourceGroup` + insert. Ping-pong A/B resources
  (FloodFillLightMap, GI history) settle into 2 cache entries; mip-cycling settles into
  2×mipCount entries. **Evicted entries are dropped from the dictionary but never
  disposed** — lifetime stays managed exactly like today's pre-baked groups (GC + render
  bundle retention, see `RenderBundleResourceLifetimeTests`), so a recorded bundle can
  never be left referencing a disposed group.

### 3.7 Pipeline layer (`PBRDeferredPipeline`)

- `CreateLightingBindGroupLayouts()` (`PBRDeferredPipeline.cs:859-926`, hand-written to
  encode depth typing): after the shader is regrouped, verify that reflection — which
  already gets depth sample types patched by `SpirvDepthTexturePatcher` +
  `MarkDepthComparisonSamplers` — produces identical layouts. If identical (expected),
  delete the hand-written layouts and create the shader via the reflection path; otherwise
  update the hand-written layouts to the packed form. Either way the "must stay in sync"
  comment goes away.
- `RebindLightingTargets` (`:940-949`): switch from numeric group ids to names
  (`"_albedo"`, `"_normal"`, ...) or cached ids from `GetResourceId`; semantics unchanged.

### 3.8 By-id API evaluation — KEEP

The numeric-id setters stay, with id redefined as an opaque dense resource ordinal:

- Every call site found treats ids as opaque: they are resolved per shader via
  `GetResourceId(name)`/`TryGetResourceId` and cached (`_shaderId_texture`, ...), or
  well-known **name** constants (`ShaderResourceId.Camera = "_camera"`, etc.) are passed to
  the string overloads. No call site hardcodes a numeric group index.
- Keeping the id overloads costs one extra array (id → location) in `ShaderReflection`.
- The XML docs on the id overloads must be updated to say "resource id obtained from
  `GetResourceId`", removing the "bind group index" wording.

## 4. Runtime churn audit (pre-implementation)

Before implementation, every runtime material-resource setter in `Src/` was audited for
call frequency and resource-identity stability. Findings, grouped by stability category:

- **Per-frame uniform updates never rebind**: all per-frame constants go through
  `GraphicsBuffer.UpdateBuffer` → `WriteBuffer` (contents only, e.g.
  `RenderingSystem.cs:255-267`, `PBRDeferredPipeline.cs:845`). Uniform buffers are Set into
  materials exactly once at creation. Unaffected by the refactor.
- **(a) Same object, unconditional per-frame Set***: `ColorGradingSystem.cs:103,109`
  (mainRT/temp, 2×/frame), `InstanceRenderer.cs:117,170` (pooled buffers, ~1/batch),
  `TextRenderer.cs:305,334` (font texture + pooled buffer), `VoxelGiRenderer`
  inject/propagate/clear/voxelize (persistent buffers, up to ~50 Set calls/frame at
  default settings). → eliminated entirely by the identity no-op check.
- **(b) Ping-pong within a fixed pair**: `FloodFillLightMap.cs:119-120` front/back — two
  persistent RenderTextures, 2 SetRenderTexture × 32 iterations = **64 Set calls/frame
  while dirty** (Sandbox/24 re-dirties every frame; TiledTerrain only on edits);
  `VoxelGiRenderer` `_radiance[0]/[1]` (2/bounce) and `_historyGI[0]/[1]` demosaic pair
  (**2 Set calls every frame**, indices flip each frame). → eliminated by the
  content-keyed cache (2 steady-state entries each).
- **Mip cycling**: `VoxelGiRenderer.BuildMipChains` re-sets the same Texture3D with
  mipLevel 0→3 (~48 Set calls/frame at defaults). The (texture, mip) tuples form a fixed
  set of 2×mipCount → cache hits after the first frame.
- **(c) Small texture pools**: `SpriteRenderer.cs:162` and `Canvas.Render.cs:48,92,115`
  set a texture per draw — the highest-frequency setters in 2D scenes. Textures come from
  a bounded asset pool → cache hits; creations only for first-use textures.
- **(d) Unbounded new objects**: none found. Every runtime path binds persistent objects
  or pooled/recycled buffers; no streaming path creates fresh resource objects per frame.
- **Worst-case estimate**: naive rebuild-on-Set = 10²–10³ `wgpuDeviceCreateBindGroup`
  calls/frame; identity check only ≈ ~120/frame; identity check + content cache ≈ 0 in
  steady state after the first frame.
- **Render bundles**: recorded bundles retain the `GPUResourceGroup` references captured
  at record time and are never auto re-recorded (`PBRDeferredPipeline.cs:491` documents
  the manual re-record contract). The cache keeps group objects alive identically to
  today's pre-baked groups, and cache hits mean bundles keep referencing the same live
  group object across rebinds.
- **Bypasses** (not affected): `ImGUIRenderer`, `FXAA`, `Bloom` bind pre-baked groups
  directly via `renderPass.SetResources(...EntrySample/EntryReadonly)`, outside
  `ShaderParameterSet`.

## 5. Implementation steps (ordered, each independently verifiable)

1. **Reflection**: implement `ShaderResourceLocation`, full resource map, dense ordinals,
   `GetLocation`/`TryGetLocation`; keep old members compiling. Unit tests in
   `Test/Alco.Graphics.Test` (extend `ShaderBindGroupValidationTests`) covering
   single-binding groups, packed groups, sampler exclusion.
2. **ShaderParameterSet v2**: slot-per-resource + identity no-op check in every setter +
   group assembly with the content-keyed cache, keeping every public signature. Unit
   tests: packed reflection info → set every resource by name and by id → assemble →
   verify entries (bindable identity per binding index) for all slot kinds incl. depth
   RT, storage-with-counter, 3D mip views; no-op setter does not dirty the group; A/B
   ping-pong across N alternations assembles exactly 2 groups.
3. **GraphicsMaterial / ComputeMaterial**: new PushResources rebuild logic, generalized
   white-texture defaulting. `dotnet build` green; existing rendering sandboxes behave
   unchanged (they all use legacy shaders whose layouts are unchanged).
4. **Core.hlsli**: group constants + `_AT` macros; legacy macros become wrappers. Extend the
   depth-macro regexes in `ShaderUtility` (+ tests in `Test/Alco.Rendering.Test`).
   `dotnet test --filter ValidateShader` must stay green — this proves the wrapper
   equivalence on all 49 legacy shaders.
5. **DeferredLighting migration**: regroup the shader (§3.3), update `PBRDeferredPipeline`
   (§3.7). ValidateShader + run the deferred sandbox; compare lighting output against a
   pre-change capture.
6. **Voxel shader migration**: regroup the five 8-set voxel shaders (+ `Voxelize.hlsl` if it
   grows) per §3.4. ValidateShader + GI sandbox run (artifacts comparison).
7. **Cleanup**: port the PoC assertions into `Test/Alco.Rendering.Test`, delete
   `artifacts/poc-bindgroup/`, update stale comments ("bind group {id}", "The target bind
   group must be a depth texture group", XML docs on id overloads).

## 6. Test plan

- `dotnet build` after every step.
- `dotnet test --filter ValidateShader` after steps 4–6 (headless DXC compile + reflection
  validation of every shader × define combination).
- New/updated unit tests:
  - `Test/Alco.Graphics.Test/ShaderBindGroupValidationTests.cs` — resource map & ordinals.
  - `Test/Alco.Rendering.Test/Shader/TestShaderBindGroupValidation.cs` — packed-group shader
    end-to-end through `ShaderUtility.CompileHLSL` (PoC assertions, minus real device).
  - ShaderParameterSet assembly tests (name/id setters on packed groups).
- Manual: deferred PBR sandbox visual diff vs pre-change screenshot; voxel GI sandbox.

## 7. Risks and mitigations

- **Depth-texture detection is name/regex based** (`SpirvDepthTexturePatcher`,
  `MarkDepthComparisonSamplers`): any new macro shape must be added to the regexes in the
  same change as the macro itself, with a ValidateShader test that would fail otherwise.
- **`-fspv-preserve-bindings` is always on**: unused declarations count toward per-stage
  budgets — keep packed declarations tight; the budget table (§3.3/§3.4) is checked per
  shader during migration.
- **Bind group churn**: addressed by design — identity no-op check + content-keyed cache
  (§3.6), validated against the runtime audit (§4); steady state ≈ 0 creations/frame.
  Cache eviction drops but never disposes entries, so recorded bundles can never be left
  referencing a disposed group.
- **Bundle lifetime**: `RenderBundleResourceLifetimeTests` pins resource lifetime behavior
  for render bundles; assembled groups must be kept alive by the parameter set exactly as
  the pre-baked ones were (same ownership, just assembled later).
- **NoDevice parity**: `NoGPU` implementations (`NoDevice`, `NoBindGroup`) must accept the
  same descriptor shapes; `ValidateShader` runs on NoGPU, so this is covered by step 4.
- **Silent id breakage**: ids change meaning (group index → resource ordinal). Mitigation:
  no literal ids exist in the codebase (verified by grep during planning); the
  `GetResourceId`-round-trip pattern is semantics-preserving.

## 8. Out of scope (documented future directions)

- **Bindless / binding arrays**: Slang's future-facing path is
  `DescriptorHandle<T>` backed by `ResourceDescriptorHeap[]`, with one material
  descriptor index selected per draw. wgpu-native can expose the required
  binding-array capabilities, while the web target still needs a bounded
  fallback. Revisit when material count per draw becomes the bottleneck.
- **Automatic physical binding assignment**: the public engine contract is
  already name-based, but today's Slang sources retain explicit
  `[[vk::binding(binding, set)]]` declarations for deterministic WebGPU layouts.
  A future offline link step may generate these positions from parameter-block
  declarations without changing C# call sites.
- **Shared sampler bank in group 0** (CRYENGINE/Godot style, samplers out of the material
  group): natural follow-up once materials move to `ALCO_GROUP_MATERIAL`; halves binding
  counts but is not needed to fix the 8-group ceiling.
- **Dynamic-offset per-draw uniforms** for group 3 (wgpu limit: 8 dynamic uniform buffers
  per pipeline layout): decide when the forward path/instancing lands.

## 9. Implementation notes (post-completion)

Completed and verified: full solution build clean, all 757 tests green, game boot
screenshot byte-identical to the pre-refactor baseline and the in-game scene visually
identical (headless game-api capture against the same save).

Two findings landed during game verification that the plan did not anticipate:

- **DXC emits implicit counter buffers named `counter.var.<name>`**. When a
  `RWStructuredBuffer` is used through a function parameter, DXC allocates a counter
  companion resource in the same set, at a binding of its own choosing (observed both at
  `owner+1` and at an unrelated free binding). The original counter detection only knew
  the `<name>_counter` suffix, so the counter leaked into the dense resource ids as a
  never-set resource and its bind group could never assemble (this broke
  `GaussianBlurWithColorGrading.hlsl` in the game). Fixed by `CounterPrefix` +
  `ShaderReflection.IsCounterCompanion`, with owner pairing always by name first —
  the binding-position fallback is unreliable for counters.
- **Fallback resolution is by resource name, not slot index.** A material instance and
  its parent may hold reflections compiled with different defines (different dense
  layouts); resolving the parent's slot by the child's slot index can read the wrong
  slot. `ShaderParameterSet.ResolveEntryValue` now maps the child's slot to its resource
  name and looks that name up in each fallback set.

New regression tests: `TestComputeMaterialInstance` (compute instance with a
counter-companion shader, mirroring the game shader shape) and
`TestShaderResourceMapping` (dense id/location mapping incl. companion exclusion and the
`vk::binding(0+1, x)` constant-expression form the `_AT` macros rely on).
