# Native Slang Migration Plan

Status: implemented. Supersedes the World3D slang proof-of-concept integration.

Implementation note: the completed runtime uses source-declared
`[[vk::binding(binding, set)]]` positions for deterministic WebGPU layouts while
preserving the name-based C# contract. This replaces the plan's proposed
set-only compiler assignment, which the pinned Slang toolchain cannot express
without a remapping pass. The migration deliberately chose explicit source
layout over retaining SPIR-V binding surgery.

Slang's direct SPIR-V emitter is the only backend. The compiler pins the
`spirv_1_3` target profile explicitly, the public options no longer expose a
via-GLSL switch, `SpirvCompat.cs` is deleted, and glslang is excluded from
runtime outputs. Generated GBuffer/Shadow/RSM/Glass material wrappers all use
the same module system, so asset/template selection cannot change the backend.
Raw scene and RSM depth reads use native `DepthTexture2D.Load`; the actual
`Depth32Float` framebuffer attachment is bound with `SetRenderTextureDepth`.
The temporary `R32Float` color mirrors and their fragment outputs are removed.
No SPIR-V binary rewriter or depth patcher remains.

Runtime bisection proved that the remaining NVIDIA/Vulkan device loss was not
invalid Slang output: the same `spirv-val`-clean bytes, including native depth
loads and the renderer's original loop control flow, run reliably when submitted
through wgpu's Vulkan SPIR-V passthrough API. The failure occurs only after the
SPIR-V is imported and re-emitted by Naga. This area has required explicit
depth-result shape handling upstream (wgpu
[#4551](https://github.com/gfx-rs/wgpu/issues/4551), fixed by
[#6384](https://github.com/gfx-rs/wgpu/pull/6384)); the validated result here
does not depend on another translator pass.

wgpu-native 29.0.1.1 exports `wgpuDeviceCreateShaderModuleSpirV`, but its C API
leaves the required `PASSTHROUGH_SHADERS` feature mapping commented out. The
pinned native patch exposes that existing wgpu-core feature; the engine requests
it when advertised and submits Slang's words unchanged on Vulkan. Patched
win-x64 and linux-x64 runtimes are bundled. Other runtime identifiers detect the
missing feature and safely retain wgpu's normal translation path.

Final validation (2026-08-23): all 92 Slang sources carry the 2025 language
pin; no `.hlsl`/`.hlsli`, DXC/DXIL native binary, legacy compiler, custom
SPIR-V reflector, or SPIR-V rewriting implementation remains. `dotnet build
Alco.slnx` completes with zero errors and the full solution test run passes
951/951 tests across 11 test assemblies. Sandbox 34's complete procedural PBR
pipeline and restored Bistro scene (1,591 draw items, 133 materials, 405
streamed texture images) each ran 60 frames on an NVIDIA RTX 4070 Ti through
Vulkan with HBAO, volumetric clouds/light, Voxel GI/RSM, SSR and Bloom enabled,
then shut down without validation errors or device loss.

Permutation cleanup (2026-08-24, D3 follow-up): dead `#if` branches
(`ALPHA_TEST`, particle/water `IS_FACADE`, water `TEXTURE_BOMBING`) were
deleted, and the engine-owned variant axes became generic value
specializations requested through `ShaderSystem.GetShader(module, args)` —
fxaa `<let Quality>` (4 presets), volumetric-cloud-noise `<let IsDetail>`,
texture-compress-bc3 `<let IsSRGB>`; the `#ifndef` default guards (HBAO,
volumetric light) became `static const`. Preprocessor defines now serve only
the material-keyword domain: `MaterialAsset.Defines`, `SHADOW_CUTOUT` (gates
varying-struct shape) and `REPEATED`. Generic modules cannot link
unspecialized, so headless validation covers them through per-module
specialization tables instead of the no-argument asset-load sweep.

## 1. Background

### 1.1 Current dxc-based pipeline (as-built)

- **Compiler**: dxc runs in-process via hand-rolled COM vtable P/Invoke
  (`Src/Alco.ShaderCompiler/Binding/Dxc/`, `DXCNative.cs` → `DxcCreateInstance`,
  `IDxcCompiler3::Compile`). Only `DxcOutKind.Object` and `Errors` are extracted;
  dxc's own reflection output is never used. Native binaries ship in
  `Src/Alco.ShaderCompiler/runtimes/<rid>/native/` (`dxcompiler.dll`, `dxil.dll`).
  The only runtime backend is wgpu-native (WebGPU) consuming SPIR-V; there is no
  D3D12/Vulkan backend.
- **Includes**: `Src/Alco.Rendering/Shader/IncludeHelper.cs` flattens `#include`
  recursively into a single translation unit (max depth 32, `#line` markers). The
  plumbed `IDxcIncludeHandler` path is effectively unused.
- **Bindings**: `Src/Alco.Rendering/Assets/Shaders/Libs/Core.hlsli` defines
  `DEFINE_*` macros expanding to `register(spaceN)` with no register number; dxc
  auto-assigns bindings sequentially per set in declaration order. C# resolves
  every resource **by name, never by binding number**
  (`docs/Shader_Binding_Slot_Collisions.md`). Compile flags rely on
  `-fspv-preserve-interface -fspv-preserve-bindings` so unused-but-declared
  resources keep their slots.
- **Reflection**: a custom SPIR-V parser (`Src/Alco.Graphics/Spirv/SpirvReflector.cs`)
  re-derives `ShaderReflectionInfo` (bind group layouts, vertex input, push
  constants, thread group size) from compiled SPIR-V. Entry points are discovered
  by regexing the source text for `[shader("vertex"|"pixel"|"compute")]`
  (`ShaderUtility.RegexFunction`, `HlslFunctionInfo.cs`).
- **Source-level conventions the pipeline depends on**: texture/sampler pairing by
  the `name##Sampler` suffix; comparison-sampler detection by name
  (`MarkDepthComparisonSamplers`); depth-texture detection by regexing
  `DEFINE_TEX2D_DEPTH*` macro calls + SPIR-V binary patching
  (`SpirvDepthTexturePatcher`, because dxc emits `OpTypeImage Depth=unknown`
  which naga rejects); structured-buffer counter companions recognized via dxc's
  implicit `counter.var.<name>` naming
  (`ShaderReflectionInfo.IsCounterCompanion`).
- **Caching / hot reload**: `ShaderCache` (`IShaderCache`) stores one file per
  (shader, defines) keyed by the XxHash64 of the *flattened* source text;
  reflection is re-derived from the cached SPIR-V on load. Hot reload goes
  through `AssetHotReloaderShaderHLSL` → re-flatten → `Shader.UnsafeHotReload(text)`
  (default permutation only, clears all caches, bumps `_version`).
- **Permutations**: plain preprocessor defines threaded from `Material.SetDefines`
  to dxc `-D`; per-permutation modules and pipelines cached by defines-string hash.
- **Assets**: `.hlsl` files are assets (`AssetLoaderShaderHLSL`,
  `AssetLoaderShaderHLSLInclude`); shaders ship as source and compile at runtime.
  `BuiltInAssets.gen.cs`/`BuiltInAssetsPath.gen.cs` hold path constants.

Inventory: 72 `.hlsl` + 19 `.hlsli` in source dirs. `Core.hlsli` is included by
61 of 72 `.hlsl` files; include depth ≤ 2. 22 compute files, no raytracing/mesh.
~50 engine passes + 14 sandbox sample shaders. 25 files use `#if` permutations.
The 10 oldest sandbox shaders lack `[shader(...)]` attributes and use explicit
`[[vk::binding]]` via a local `SLOT` macro.

### 1.2 Existing slang beachhead (World3D, branch `slang`)

`Src/Alco.World3D/Rendering/Slang/` already compiles the whole World3D pipeline
set through slang (Sandbox 34 runs all-slang) and proves the material-model
direction: `ISurface` interface + generic pass entry points
(`GBufferMainVS<Surface>` in `Assets/ShadersSlang/Pipelines/gbuffer.slang`)
replace the `@SURFACE@` text splice, and `_materialParams` packing uses
slang-reflected member offsets.

It also proves what must **not** be carried into the final design:

- It binds the **deprecated flat C API** (`spCreateSession`/`spCompile`/
  `spGetReflection`, `SlangNative.cs`); slang.h now marks `ICompileRequest`
  `[[deprecated]]`.
- It accumulates post-compile SPIR-V surgery: `SlangBindingRemapper` (rewrites
  `DescriptorSet`/`Binding` decorations because slang rejects the set-only
  `register(spaceN)` syntax), `SlangBaseInstanceZeroer` (wgpu rejects
  `gl_BaseInstance`), redundant-`DrawParameters`-capability stripping,
  `-emit-spirv-via-glsl` for everything except one shader whose glslang output
  naga rejects, and `SlangSpirvFacts` (re-reads thread group size / storage
  formats from SPIR-V because the flat reflection path doesn't expose them).
- It routes around the engine facilities (provider-mode `Shader` ctor) instead of
  through them; engine built-ins and the glass material pass remain dxc-only.

### 1.3 Why migrate

dxc replacement is the least of it. The goals are: modules/import instead of
textual include flattening; interfaces + generics instead of macro permutations
and text splicing; `ParameterBlock<T>` instead of the `DEFINE_*` macro layer;
first-class slang reflection instead of a hand-maintained SPIR-V parser plus
source regexes; and a single-source path to future targets (WGSL for a web/Dawn
build) via the capability system.

## 2. Goals and non-goals

**Goals**

1. All engine, World3D and sandbox shaders compile as native slang modules.
2. The compile/runtime/reflection stack is redesigned around the modern slang
   API (`IGlobalSession`/`ISession`/`IModule`/`IComponentType`); dxc, dxc
   bindings, `IncludeHelper`, and the custom SPIR-V reflector are removed.
3. The shader-facing binding contract stays name-based and keeps the
   frequency-grouped set layout from `docs/MaterialBindGroupRefactorPlan.md`.
4. Every phase is independently green: `ValidateShader`, unit tests, and
   screenshot-diff validation against pre-change captures.

**Non-goals**

- No new GPU backend (wgpu remains the only one); multi-target output is kept
  possible, not built.
- No bindless rewrite of the material system (slang `DescriptorHandle<T>` is
  noted as a future direction only).
- No dynamic dispatch (`dyn`) in materials; static specialization only.
- No visual/behavioral changes to rendering output.

## 3. Key design decisions

### D1 — A dedicated ShaderSystem owns modules; the asset system is demoted to file provider

slang `import` is a compiler-domain concept: modules are resolved by name against
session search paths, cached per session, compiled separately to IR, and their
dependency graph is queryable (`IModule::getDependencyFileCount/Path`). The
per-file asset-loader model ("one `.hlsl` → flatten → one `Shader`") has no place
for a module graph, for `.slang-module` binary artifacts, or for reverse
invalidation (a changed lib must invalidate its importers, not itself).

Therefore:

- **ShaderSystem** (new, §4.2) owns the slang global session, sessions per
  search-path set, the module cache, the `.slang-module` disk cache, dependency
  tracking, diagnostics, and hot-reload invalidation. Callers ask for
  `GetShader(moduleName, specialization)` — not `Load<Shader>(path)`.
- The asset system keeps exactly two roles: (a) backing the slang virtual file
  system (`ISlangFileSystemExt`, evolution of `SlangFileSystem.cs`) so pak files,
  embedded assets and the directory watcher keep working — slang imports are
  fully virtualizable; (b) file-change notifications that feed ShaderSystem's
  reverse-dependency invalidation.
- `AssetLoaderShaderHLSL`, `AssetLoaderShaderHLSLInclude` and `IncludeHelper` are
  deleted at teardown. `BuiltInAssets*.gen.cs` switches from shader path
  constants to module name constants.

### D2 — Binding model: explicit set index, compiler-assigned binding within set, name-based resolution

Keep the current philosophy — slang's semantics are compatible and stronger:

- slang assigns bindings **before dead-code elimination**, so unused parameters
  keep their slots and layouts are stable across specializations. This subsumes
  the `-fspv-preserve-bindings` behavior the engine relies on, with no flag.
- Sets are explicit and follow the frequency convention
  (`MaterialBindGroupRefactorPlan.md` §3.1): 0 = frame, 1 = pass, 2 = material,
  3 = draw. In slang sources a set is expressed by `ParameterBlock<T>` placement
  / `register(..., spaceN)` / `[vk::binding(b, s)]` as appropriate; within a set,
  binding numbers are compiler-assigned in declaration order — never written by
  hand, never read by C#.
- C# continues to resolve resources by name through `ShaderReflectionInfo`;
  `ValidateBindGroupLayouts` (group count ≤ limit, contiguity) is unchanged.
- The `DEFINE_*` macro layer, the `name##Sampler` pairing convention and the
  `SLOT` macro all retire. Combined texture samplers / explicit
  `SamplerState`/`SamplerComparisonState` declarations replace them.

### D3 — Permutations: generic value parameters + link-time specialization instead of preprocessor defines

slang preprocessor macros are **session-global**; official guidance is to build
variants with generics and specialization instead. Concretely:

- `#if VOXEL_MAX_LEVELS==6`-style switches become `void MainCS<let MaxLevels : int>(...)`
  and are instantiated via `IComponentType::specialize(SpecializationArg)`.
  Specializations are type-checked before codegen and produce stable reflection.
- Material composition is interface specialization (`ISurface`), already proven
  in World3D.
- `#define` survives only for true global compile-time switches during the
  transition; `Shader.TestAllDefines` becomes "enumerate specializations".
- Side benefit: kills the per-define-set compile-request/session overhead the
  current beachhead pays.

### D4 — slang reflection (`ProgramLayout`) becomes the single source of truth

- Bind group layouts are built through the **binding ranges API**
  (`TypeLayoutReflection.getBindingRangeCount/Type`,
  `getFieldBindingRangeOffset`, …) — the sanctioned cross-target route that maps
  directly to descriptor types, replacing both the custom SPIR-V walk and any
  register arithmetic.
- Entry points come from `IModule.getEntryPointCount/getEntryPoint` +
  `EntryPointReflection` (stage, thread group size, varying I/O with semantics,
  push-constant ranges). Source-regex entry discovery is deleted.
- Per-parameter liveness after DCE is checked with
  `IMetadata.isParameterLocationUsed` when exact budget accounting matters.
- `ShaderReflectionInfo` **keeps its current shape** (name → dense ordinal →
  `ShaderResourceLocation`; `BindGroups`; `VertexLayouts`; push-constant size) —
  only its producer changes. This is the deliberate minimal-intrusion decision:
  `Shader.GetGraphicsPipeline`, the pipeline caches, and all of
  `ShaderParameterSet` (slot-per-resource, identity no-op, content-keyed group
  cache, name-based fallback) are untouched.
- `SpirvReflector` survives the transition as a cross-check harness only, then
  is deleted (Phase 3/4).

### D5 — Language version and module conventions

- Transition period: sources stay on the default legacy language version
  (HLSL-like defaults, minimal friction); slang can `import` plain `.hlsl` files
  as legacy all-public modules, so old and new code intermix freely.
- Every **new/rewritten** module starts with a `module Alco.*;` declaration and
  pins `#language slang 2025` (module declaration required, `internal` default
  visibility). 2026 semantics (`dyn` keyword etc.) are deferred until the
  migration settles.
- Module namespace mirrors assembly + directory:
  `Alco.Rendering.Core` (was `Core.hlsli`), `Alco.Rendering.PostProcess.*`,
  `Alco.World3D.Pipelines.*`, `Alco.World3D.Materials.*`.
- slang version is **pinned** (recorded in §4.1) and upgraded deliberately.
  Profile/capability IDs are not stable across releases — always resolved by
  name (`findProfile`/`findCapability`).

### D6 — Strangler transition, not a flag day

dxc stays available behind the existing provider seam until Phase 4. During
Phases 1–3 the same shader can be compiled by both toolchains for A/B
validation (SPIR-V comparison + screenshot diffs — the workflow already used
for `artifacts/sandbox34-all-slang-*`). No dual-stack remains after Phase 4.

## 4. Target architecture

### 4.1 Compile stack (`Src/Alco.ShaderCompiler`)

- New `Binding/Slang/` next to `Binding/Dxc/`, same hand-rolled COM-vtable style
  as `DXCNative.cs` (slang interfaces are COM-shaped; `IBlob` is
  `ID3DBlob`-compatible). Surface: `createGlobalSession`
  (`slang_createGlobalSession2`), `ISession` (targets, search paths, macros,
  `fileSystem`, compiler options), `IModule`, `IComponentType` (composite, link,
  specialize, `getLayout`, `getEntryPointCode`), `ISlangFileSystemExt`.
- One shared `IGlobalSession`; per-(search-path-set) `ISession`. Consider
  `slang_createGlobalSessionWithoutCoreModule` + embedded core module for startup
  cost if profiling shows it matters.
- Diagnostics: every call collects its `IBlob**` diagnostics blob (non-null even
  on success — carries warnings), surfaced with file/line into the engine's
  shader error reporting. Replaces error-string scraping.
- slang native binaries move from `Src/Alco.World3D/runtimes/` to
  `Src/Alco.ShaderCompiler/runtimes/<rid>/native/`, shipped by
  `Alco.ShaderCompiler.csproj` exactly like `dxcompiler.dll` today. The pinned
  slang version is recorded at the top of `Binding/Slang/`.
- Managed facade: `SlangCompiler` with operations
  `LoadModule(name)`, `Compile(module, entries, specialization, target)` →
  per-entry SPIR-V + `ProgramLayout`. No SPIR-V post-processing hooks in the
  new API (see Phase 3 for retiring the existing ones).

### 4.2 ShaderSystem (new runtime service, `Src/Alco.Rendering/Shader/`)

Responsibilities:

- Module cache keyed by module name; dependency graph from
  `IModule.getDependencyFilePath`; reverse-dependency invalidation on file-change
  notifications (replaces `AssetHotReloaderShaderHLSL` + `UnsafeHotReload(text)`;
  a lib edit now invalidates exactly its importers, and every compiled
  specialization of them — not just the default permutation).
- Disk cache replacing `ShaderCache`: two layers. (a) `.slang-module` IR blobs
  (`IModule.serialize` / `ISession.loadModuleFromIRBlob`,
  `isBinaryModuleUpToDate`); (b) linked-program cache keyed by
  (module IR hash, entry set, specialization args, target, slang version).
  **Caveat from the slang docs**: a binary module whose primary `.slang` source
  is absent from the search paths is accepted as up-to-date without validation —
  shipped builds must either include sources or embed an explicit version stamp
  in the cache key.
- `GetShader(moduleName)` / `GetShader(moduleName, specialization...)` returning
  the unified `Shader` (§4.4). Built-in shaders register by module name
  (`BuiltInAssetsPath.gen.cs` regeneration).
- Hot reload: watcher event → map file path → module(s) → invalidate →
  `Shader` `_version` bump → lazy pipeline rebuild via the existing
  `TryUpdatePipelineContext` mechanism.

### 4.3 Shader source organization

- One `.slang` file per module, `module` declaration first line, `public` only at
  API boundaries. Shared libs under `Libs/` become real modules
  (`Alco.Rendering.Core`, `Alco.Rendering.ReversedDepth`, `Alco.World3D.VoxelCommon`,
  …) — import graph replaces the include graph; no include guards, no
  declaration-order coupling.
- Binding declarations use plain slang resource types in `ParameterBlock<T>`
  groupings per frequency set (D2). The frequency constants live in
  `Alco.Rendering.Core`.
- Entry points keep `[shader("vertex"|"fragment"|"compute")]` +
  `MainVS/MainPS/MainCS` naming; the 10 legacy sandbox shaders gain attributes.
- Depth textures and comparison samplers are declared with their real slang
  types (no macro marker, no name convention). If any naga-facing SPIR-V gap
  remains (Phase 3 verifies), it is annotated with a user-defined attribute
  (`[AlcoDepth]`, reflectable via `findUserAttributeByName`) — never with
  source regexes.
- Structured-buffer counters: declare counter buffers explicitly where needed,
  or rely on slang reflection's explicit representation — either way the
  `counter.var.<name>` name-pairing logic dies (Phase 3).

### 4.4 Runtime `Shader` / pipeline layer

- The two `Shader` construction modes (text-mode dxc pipeline vs provider
  callback) unify into one: a `Shader` is **(module name, entry points,
  specialization)** produced by ShaderSystem. `RenderingSystem.CreateShader(text)`
  and the provider ctor are both removed at teardown.
- `GetGraphicsPipeline` / `GetComputePipelineInfo` signatures and cache-key
  structure are unchanged; "defines" in keys become specialization identity.
- `Precompile` and `TestAllDefines` become specialization enumeration
  (`TestAllSpecializations`), still driven from the module source.

### 4.5 Material system

`ShaderParameterSet`, `Material`, `GraphicsMaterial`, `ComputeMaterial`,
`MaterialInstance` are **unchanged** — they consume `ShaderReflectionInfo`, whose
shape is preserved (D4). Changes are confined to composition:

- `MaterialCompiler`: the `@SURFACE@` text splice and the HLSL-surface float4
  regex packing are deleted. Every material surface is an `ISurface`
  implementation; per-(asset, pass) shaders are generic instantiations;
  `_materialParams` packing continues via slang-reflected member offsets and is
  promoted out of World3D into the shared path.
- Material parameter block becomes `ParameterBlock<MaterialParams>` in set 2,
  matching the frequency-group design.
- The glass pass (`MaterialCompiler.cs` "HLSL-only for now") gets a slang
  template like the other passes.

## 5. Slang feature adoption map

| Current pattern | Replaced by |
|---|---|
| `#include` / `.hlsli`, `IncludeHelper` flattening | `module` + `import`, `namespace Alco.*` |
| `DEFINE_UNIFORM/TEX2D_SAMPLE/STORAGE/...` macros | plain declarations grouped in `ParameterBlock<T>` per frequency set |
| `name##Sampler` pairing, comparison-sampler name detection | `SamplerState` / `SamplerComparisonState` declarations; reflected kinds |
| `#if` / define permutations, `TestAllDefines` | generic value params (`<let N : int>`) + `specialize`; `static const` |
| `@SURFACE@` text splice, surface-by-convention | `ISurface` interface + generic entry instantiation (already proven) |
| Macro constants (`VOXEL_*`, cloud/atmosphere) | `static const` / constexpr |
| `__SLANG__` / capability `#ifdef` guards | `[require(...)]` capabilities, `__target_switch` where unavoidable |
| Regex depth-texture markers | real depth texture types; `[AlcoDepth]` user attribute as fallback marker |
| `counter.var.<name>` pairing | explicit counter buffers or slang-reflected counters, paired by reflection |
| `register(spaceN)` auto-binding | explicit sets via parameter blocks; compiler-assigned bindings within set |
| Per-type helper duplication | `extension` methods, `typealias`, properties, free-function operators |
| dxc shader models | profiles by name + capability atoms |

Deliberately not used: dynamic dispatch (`dyn`), GPU pointers/lambdas, RT/mesh
features (no such passes exist), experimental APIs beyond user-defined
attributes.

## 6. Phase plan

Each phase ends green: full `dotnet build`, `dotnet test`, and for anything
touching compiled output a screenshot/artifact diff against the pre-phase
capture.

### Phase 0 — Modern slang API foundation

1. Pin the slang release; move native binaries to
   `Src/Alco.ShaderCompiler/runtimes/<rid>/native/` + csproj shipping (mirroring
   the dxc entries).
2. Implement `Binding/Slang/` COM bindings (global session, session, module,
   component type, reflection, file-system ext, diagnostics) in the `DXCNative.cs`
   style.
3. Implement `SlangCompiler` facade + `ISlangFileSystemExt` adapter over the
   engine file source (evolving `SlangFileSystem.cs`).
4. Parity harness (test project): compile a representative shader set
   (2D, postprocess, one World3D pipeline, one material) through dxc-path and
   slang-path; compare reflection and (where deterministic) SPIR-V. This harness
   becomes the A/B tool for Phases 2–3.

*Exit*: slang modern API compiles engine shaders headlessly; dxc untouched.

### Phase 1 — ShaderSystem and module infrastructure

1. ShaderSystem: module cache, dependency graph, reverse-dependency invalidation,
   `.slang-module` + linked-program disk cache (replaces `ShaderCache` format),
   watcher integration.
2. Unified module-backed `Shader` construction alongside the existing modes
   (third, temporary mode; old modes deleted in Phase 4).
3. Port `Core.hlsli` → `Alco.Rendering.Core.slang` (parameter blocks, real
   sampler types, frequency-set constants). `Core.hlsli` stays for the dxc path.
4. Hot reload through module invalidation; `UnsafeHotReload` gains a module-based
   path.

*Exit*: a sandbox sample can run fully on module-loaded slang shaders with hot
reload; caches validated by unit tests (hit/miss/invalidation/staleness).

### Phase 2 — Shader source migration (per directory, any order within)

1. `Alco.Rendering` Libs (`ReversedDepth`, common math/tonemap utils) → modules.
2. 2D + postprocess + compute-utils pipelines (32 files: Sprite, Text, TileMap,
   Particle, Bloom, Tonemap×6, FXAA, ColorGrading, Blit, GaussianBlur, BC3,
   FloodFill, TextSDF, ClearTexture) and `ImGui.hlsl`; normalize entry attributes
   on legacy sandbox shaders (samples 1–11, 23, 24).
3. World3D libs + PBR pipelines (26 files + 8 hlsli): VoxelGI×10, SSR×4, HBAO,
   clouds, DeferredLighting, GBuffer, ShadowDepth, RSM, ForwardGlass. Define
   permutations convert to generic value parameters (D3).
4. Material system cut-over: all surfaces to `ISurface`; delete `@SURFACE@`
   splice and HLSL float4 regex packing; glass pass slang template;
   `ParameterBlock<MaterialParams>` in set 2.
5. Retire the beachhead files as their coverage is subsumed:
   `SlangPipelineShaderFactory`, World3D `SlangShaderCompiler`/`SlangNative`
   superseded by the shared stack.

*Exit per directory*: `ValidateShader` (slang mode) + sandbox screenshot diff.
*Phase exit*: zero `.hlsl`/`.hlsli` in `Src/`; dxc path unused by default.

### Phase 3 — Reflection cut-over and SPIR-V surgery removal

Ordered, each behind the Phase-0 parity harness:

1. `ShaderReflectionInfo` producer switches to slang `ProgramLayout` (binding
   ranges API; entry-point varying I/O; push constants; thread group size via
   `EntryPointReflection`). `SpirvReflector` becomes a cross-check in tests.
2. Delete `SlangBindingRemapper` — bindings now expressed in source (D2);
   determinism of in-set assignment pinned by tests.
3. Delete `SlangSpirvFacts` (reflection now covers thread group size; storage
   formats via type-layout resource shape/access — verify).
4. Verify and delete workarounds one by one against the pinned slang version:
   `SlangBaseInstanceZeroer`, DrawParameters-capability stripping,
   `-emit-spirv-via-glsl` (including the `ScreenSpaceReflectionBlueNoise`
   exception). No compatibility backend remains; direct SPIR-V is mandatory.
5. Depth textures: verify slang emits a naga-accepted `Depth` operand for the
   engine's depth-texture declarations; if not universally, keep a minimal
   patcher driven by slang reflection/`[AlcoDepth]` — source regexes are deleted
   either way.
6. Comparison samplers: reflect `SamplerComparisonState` directly; delete
   `MarkDepthComparisonSamplers` and the `SamplerSuffix` convention.
7. Counters: re-derive owner pairing from slang reflection; delete the
   `counter.var.` / `_counter` name logic and the binding-adjacency fallback.

*Exit*: no SPIR-V binary rewriting on any shader; `SpirvReflector` only in
tests; all rendering sandboxes screenshot-clean.

### Phase 4 — Teardown

1. Delete dxc: `Binding/Dxc/`, `ShaderCompilerDxc`, dxc/dxil binaries,
   `FileExt.cs` dxc-only entries.
2. Delete `IncludeHelper`, `AssetLoaderShaderHLSL(Include)`, text-mode `Shader`
   ctor, provider ctor, `UnsafeHotReload(text)`, regex entry discovery,
   `SpirvReflector` (post cross-check), `SpirvDepthTexturePatcher` remnants.
3. `BuiltInAssets` generator emits module names; `GameEngine.Loader.cs` drops
   shader loaders.
4. Docs: update `Shader_Binding_Slot_Collisions.md` (binding semantics now
   slang-defined), `MaterialBindGroupRefactorPlan.md` (§8 note: bindless future
   via `DescriptorHandle<T>`/`ResourceDescriptorHeap`), add a slang coding
   standard (module naming, visibility, specialization-vs-define policy).

## 7. Test and validation plan

- `ValidateShader` becomes slang-based: every module × every specialization
  compiles headlessly; reflection conventions validated (naming, set usage,
  budget limits per wgpu defaults).
- New unit tests: ShaderSystem cache (hit/miss, dependency invalidation,
  `.slang-module` staleness), slang→`ShaderReflectionInfo` mapping (packed
  groups, sampler kinds, counters, vertex inputs, push constants), binding
  determinism across specializations.
- Convention tests: every `.slang` starts with `module` + language pin; every
  entry point has `[shader(...)]`; no `register` without a set; no `#include`
  in `Src/`.
- A/B while dxc exists: parity harness + screenshot diffs (deferred PBR sandbox,
  voxel GI sandbox, 2D/Canvas sandbox, boot screenshot) against pre-phase
  captures, per the established artifacts workflow.
- Optional CI hardening: `slangc -depfile` for offline dependency checking of
  the module graph.

## 8. Risks and mitigations

- **slang SPIR-V vs naga/wgpu gaps** (previously observed: `BaseInstance` and
  `DrawParameters`). Mitigation: pinned version and direct-output validation;
  do not add a second compiler backend as a workaround.
- **Depth-texture `Depth` operand**. Mitigation: dedicated reflection tests prove
  that `DepthTexture2D` maps to WebGPU's depth sample type; the renderer binds
  the real depth attachment, with no mirror or binary patcher.
- **Naga SPIR-V import/re-emission on Vulkan**. Valid Slang output containing
  native depth loads and ordinary loop control flow caused device loss only
  after the Naga round trip. Mitigation: request wgpu-core's existing
  `PASSTHROUGH_SHADERS` feature and submit validated SPIR-V directly to Vulkan;
  the C API exposure is a pinned, reproducible wgpu-native patch rather than a
  shader rewrite or alternate compiler backend.
- **Vertex input layout drift** (semantics vs the current Location-scan packing).
  Mitigation: parity harness compares `VertexInputLayout` for every migrated
  shader before cut-over.
- **Session-global macros vs define permutations mid-transition**. Mitigation:
  Phase 2 converts permutations to specialization *before* those shaders move;
  interim define sets use dedicated sessions (accepted cost, bounded lifetime).
- **Binary module cache staleness** (primary source absent → accepted as
  up-to-date). Mitigation: explicit (slang version + options) stamp in our cache
  keys; shipped builds keep sources or stamp.
- **Counter pairing regression** (the `counter.var.` incident class).
  Mitigation: dedicated tests mirroring `GaussianBlurWithColorGrading` and the
  compute-instance counter shapes before the name logic is deleted.
- **Compile-time regression**. Mitigation: module cache + IR blobs are expected
  to *improve* over whole-TU recompiles; measure boot compile time before/after
  Phase 1 and keep the numbers in the phase notes.

## 9. Out of scope / future directions

- WGSL output for a Dawn/web build (capability system + `ParameterBlock` mapping
  already keep the door open).
- Bindless materials via `DescriptorHandle<T>` + `ResourceDescriptorHeap[]`.
- Slang language version 2026 adoption (`dyn`, tuple changes) after stabilization.
- Precompiled shader shipping (offline `slangc` pipeline producing
  `.slang-module`/linked binaries; runtime JIT path stays for the editor).
- Obfuscated module serialization for shipped builds.
