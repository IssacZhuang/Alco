# Slang coding standard

Alco compiles shaders with the modern Slang API and the pinned Slang 2026.1.6
native runtime. These rules apply to engine, World3D, sandbox and test shader
modules.

## Module header and naming

Every `.slang` file contains exactly one language directive followed by exactly
one module declaration:

```slang
#language slang 2025
module DeferredLighting;
```

Comments may precede the directive.

File names and module names are **PascalCase and identical** — the module name
is the file stem (`DeferredLighting.slang` ↔ `module DeferredLighting;`). A
name spells the concept the way the engine's C# code spells it, so acronyms
stay intact: `FXAA`, `HBAO`, `SSR`, `VoxelGI`, `ImGUI`, `AlcoWorld3D_*`.
`SlangSourceConventionTest` enforces the exact stem/module pairing and rejects
textual includes, `register(...)` declarations, `[[vk::binding]]` decorations,
sampling macros, and cbuffer blocks outside push constants.

Imports use identifier syntax (`import AlcoRendering_Core;`) and must match the
imported module's file stem case-exactly. The engine resolver probes
case-insensitively, so the convention test pins case-exact imports — a typo
fails at compile time instead of silently resolving.

## Prefix rule: libraries vs pass modules

- **Library modules** — importable, cross-pass code — carry an assembly
  namespace prefix and exactly one underscore: `AlcoRendering_Core`,
  `AlcoRendering_GaussianBlur`, `AlcoWorld3D_Surface`, `AlcoWorld3D_PBRCommon`.
  The prefix matches the owning C# assembly (`Alco.Rendering`,
  `Alco.World3D`). Slang module names are global at runtime (both assemblies'
  shader trees merge into one `Assets/` namespace), so the prefix is what keeps
  engine imports from colliding with game content.
- **Pass/material modules** — entry points and surface templates, never
  imported by other shader modules — are bare PascalCase: `FXAA`, `Blit`,
  `Voxelize`, `PbrStandard`. They are referenced by name from render-node
  assets (`.rnfact`) and C#, not from `import` statements.

## Directory layout

Directories express responsibility; the prefix expresses importability:

```
Assets/Shaders/
  Libs/          AlcoXxx_* library modules (the importable surface)
  Materials/     surface modules composed by the material compiler
  Passes/        entry-point modules, grouped by render-node cluster:
    Rendering/   Blit/, Scene/ (2D: sprite, text, particle, tile…)
    PostFX/      FXAA, ColorGrading, Bloom/, Tonemap/
    Compute/     ClearTexture, GaussianBlur*, TextSdf, Compress/
    Deferred/    GBuffer, ShadowDepth, Rsm, Glass, DeferredLighting
    HBAO/        HBAO, HBAOBlur
    SSR/         SSRTrace, SSRResolve, SSRComposite, SSRBlueNoise, SSRDepthDownsample
    VoxelGI/     Voxelize, Voxel*
    Volumetrics/ Volumetric*
```

A module's directory position is never load-bearing — the engine resolver
answers module-name probes wherever the file sits — but keep new files in the
cluster their owning render node lives in (`Assets/RenderNodes/*.rnfact`
mirrors `Passes/<cluster>/`).

## ParameterBlock and binding contract

Rendering and World3D validation compile every entry-point module and inspect
Slang reflection headlessly.

- A surface declares its resources in its own `ParameterBlock` (any block
  name; the material set is wherever the composed layout puts it — the engine
  reads texture slots from the surface module's reflection, not a set number).
  The engine binds members by bare name.
- One block = one whole descriptor set; blocks take sets in declaration order
  (entry module blocks first, then companion modules', then imported modules').
  Bare globals (no block) fill set 0 before any block — new code should not mix
  them; wrap globals in a block.
- A block with ordinary data gets an automatically-introduced uniform buffer at
  binding 0 under the block variable's name; resource members continue after it
  in declaration order. A resource-only block emits no UBO and its members start
  at binding 0.
- Blocks and their structs need `public` members when shared across modules
  (Slang 2025 visibility is internal by default).
- Block members keep their bare field names in reflection (`_output`, not
  `_pass._output`) — the shader body qualifies (`_pass._output`), but the
  engine binds by the bare name; binding numbers are private physical layout
  and must not appear in caller logic.

When a pass needs an unfiltered raw depth value, declare `DepthTexture2D`, call
`Load`, and bind the framebuffer's native depth attachment with
`SetRenderTextureDepth`. Do not add a color depth mirror or an explicitly
formatted `Texture2D<float>` substitute.

Direct SPIR-V compiler output is mandatory. Do not select a compiler backend in
shader-loading code or introduce a via-GLSL/glslang fallback. On Vulkan,
wgpu-native's SPIR-V passthrough capability submits the validated Slang output
without a Naga import/re-emission round trip. Non-Vulkan backends retain wgpu's
normal target translation path.

## Shared samplers

Modules never declare per-texture sampler companions. Sampling goes through the
shared sampler bank of `AlcoRendering_Core` - a `ParameterBlock<SamplerBankParams>`
named `_samplers` that every core import reflects into its own layout:

```slang
import AlcoRendering_Core;
...
float4 c = _albedoTexture.Sample(_samplers._linearRepeat, uv);
```

- Bank members are the contract, referenced by name: `_linearClamp`, `_linearRepeat`,
  `_nearestClamp`, `_nearestRepeat`, `_linearMirrorRepeat`, `_nearestMirrorRepeat`,
  `_anisotropicClamp` (8x), `_anisotropicRepeat` (8x) and `_depthComparison`
  (`SamplerComparisonState`, LessEqual, clamp).
- Convention: screen-space passes and render-texture reads use `_linearClamp`;
  material/asset textures default to `_linearRepeat`; shadow comparison uses
  `_depthComparison`.
- The engine binds the bank from `RenderingSystem.Samplers` (`SharedSamplers`);
  the GPU device only creates raw samplers (`CreateSampler`). Textures never carry
  sampling state - a texture and its sampling are independent resources.
- The bank is immutable engine-wide state and is never overridable: the library
  serves one shared sampler-only bind group per layout and every material binds it
  as-is; bank member names are reserved and rejected by `SetSampler`.
- Custom sampling stays explicit: a module declares its own `SamplerState` member
  (never a bank member name) and the material binds a sampler to that name through
  `ShaderParameterSet.SetSampler`. A custom name left unbound fails loudly at bind
  group assembly - never silently.

## Validation

Before landing shader changes, run:

```text
dotnet build Alco.slnx --no-restore
dotnet test Test/Alco.ShaderCompiler.Test/Alco.ShaderCompiler.Test.csproj --no-build
dotnet test Test/Alco.Rendering.Test/Alco.Rendering.Test.csproj --no-build
dotnet test Test/Alco.World3D.Test/Alco.World3D.Test.csproj --no-build
```

`SlangSourceConventionTest` enforces module headers, the PascalCase
stem/module pairing, case-exact imports, and rejects legacy HLSL, textual
includes, `register(...)` annotations, `[[vk::binding]]` decorations, and
sampling macros. `SlangBlockBindingTest` pins the block reflection contract
(bare member names, compiler-assigned order, multi-block sets). Rendering and
World3D validation compile every entry-point module and inspect Slang
reflection headlessly.
