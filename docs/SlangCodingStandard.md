# Slang coding standard

Alco compiles shaders with the modern Slang API and the pinned Slang 2026.16
native runtime. These rules apply to engine, World3D, sandbox and test shader
modules.

## Module header and naming

Every `.slang` file contains exactly one language directive followed by exactly
one module declaration:

```slang
#language slang 2025
module alco_rendering_example;
```

Comments may precede the directive. File names are lowercase kebab-case
(`alco-rendering-example.slang`, `gaussian-blur-rgba16f.slang`, `fxaa.slang`);
module names are the file stem in lowercase snake_case and pair with it exactly
(`module gaussian_blur_rgba16f;`, `module fxaa;`). Acronyms stay intact —
`fxaa`, never `f_x_a_a`. The pairing keeps slang's import probing
(underscore ↔ dash) resolvable on case-sensitive asset systems (Linux/Android)
and is enforced by `SlangSourceConventionTest`. Prefix shared modules with
their owning assembly (`alco_rendering_`, `alco_world3d_`); the engine-side
load name is the dashed stem, so `BuiltInAssets` exposes each pipeline shader
as `Shader_` + the stem PascalCased per dashed word (`Shader_GaussianBlurRgba16f`).

Use `import`; `#include` is forbidden. Export only the declarations another
module consumes. Under Slang 2025 visibility is internal by default, so shared
types and functions must be marked `public` deliberately.

## Identifier casing

Types (struct, interface, enum) are UpperCamelCase (`ShadowVertex`, `ISurface`)
following the official Slang conventions. Values are lowerCamelCase, with two
deliberate house-rule exceptions inherited from the HLSL lineage: entry points
and interface methods stay PascalCase (`MainVS`, `GetBaseColor`) because C#
looks them up by name, and module-scope shader resources keep the `_` prefix
(`_texture`, `_instances`) to keep them visually distinct from locals. Static
constants are SCREAMING_SNAKE_CASE (`PI`, `ALCO_GROUP_FRAME`). Acronyms are
written all-upper or all-lower, never title-cased: `instanceID`, `normalTS`,
not `instanceId`.

## Entry points and variants

Entry points are named `MainVS`, `MainPS` or `MainCS` and carry an explicit
stage attribute:

```slang
[shader("fragment")]
float4 MainPS(Varyings input) : SV_TARGET { /* ... */ }
```

Use `vertex`, `fragment` and `compute`; do not add the HLSL alias `pixel`.
Compute entry points also declare `[numthreads(x, y, z)]`.

Variant axes split by owner. Engine-owned variant axes (fxaa quality,
sRGB compression, cloud-noise bake kind) are generic value parameters:
the entry point declares `<let Quality : int>` and the C# owner requests a
specialized shader through `ShaderSystem.GetShader(module, args)` — the
arguments are slang expressions (`"0"`, `"1"`, type names). Never convert
these back to `#if` permutations. Preprocessor defines are reserved for the
material-keyword domain only: user-authored `MaterialAsset.Defines` and
`REPEATED` (a per-material texture-wrap toggle). Do not introduce a new
`#if` permutation outside that domain. ShaderSystem specialization
arguments are part of the program cache identity.

## Material composition (World3D surfaces and pass templates)

Materials are slang types, not string permutations. A material is a struct
implementing `ISurface` (`Libs/alco-world3d-surface.slang`); a pass is a
template module whose entry points are generic over the surface:

```slang
[shader("vertex")] public MainVOut MainVS<T : ISurface>(MainVIn v) { ... }
```

Composition is `specialize(entryPoint, surfaceType)` + link — there is no
generated wrapper shader anywhere in the pipeline. The rules:

- Surface interfaces are fine-grained (`IVertexSurface`, `IAlbedoSurface`,
  `INormalSurface`, `IMaterialPropsSurface`, `IEmissiveSurface`,
  `IVoxelFeedSurface`) with full default implementations; `ISurface`
  aggregates them. A surface overrides only what it needs, and every
  override must carry the `override` modifier (Slang error 36107
  otherwise) — intent is explicit.
- Behavior branches inside a pass template use **value specialization**
  (`where let AlphaTest : bool`), requested from C# via
  `MaterialPassDesc.ValueSpecArgs`. This is the same mechanism as
  engine-owned generic value parameters; retired textual permutations
  (e.g. `SHADOW_CUTOUT`) must not come back.
- Surface-declared resources follow the same set-scoped cbuffer-block
  convention as everything else, in the material set (space2,
  `MaterialCompiler.SurfaceResourceSet`): `cbuffer _material :
  register(b0, space2) { Texture2D _albedoTexture; ... }`. The engine
  binds members by bare name. The `[[vk::binding]]` ban applies to
  surfaces too — block + set is the whole contract.
- Pass templates keep their engine resources in the low sets (frame 0,
  pass 1, draw 3) per the rules above; the surface module owns space2
  alone.
- Template entry points must be `public` and carry the `[shader]` stage
  attribute so the composer can find them without a wrapper.

See `docs/MaterialSystem.md` for the C# side (`MaterialComposer`,
`MaterialCompiler`, pass registration, texture-slot and params-block
rules).

## Resources and bindings

Use real Slang resource types (`Texture*`, `SamplerState`,
`SamplerComparisonState`, structured buffers and storage textures). New code
must not add `DEFINE_*`, `SLOT`, sampler-token-concatenation or depth-marker
macros.

Resources are declared inside **set-scoped cbuffer blocks** — the shader states
only which set it owns, and Slang assigns member bindings in declaration order:

```slang
cbuffer _pass : register(b0, space1)
{
    Texture2D _sceneColor;      // binding 0 in the set
    SamplerState _sceneSampler; // binding 1
    RWStructuredBuffer<float4> _output; // binding 2
};
```

Never write `[[vk::binding(binding, set)]]`; it pins every member and defeats
the convention (`SlangSourceConventionTest` rejects it). The rules:

- One set = one block. A block without uniform data emits no UBO; members take
  the set's bindings from zero. A block with uniform data emits its buffer at
  the block's register (`b0`) and members continue after it.
- Sets are frequency grouped: frame 0, pass 1, material 2, draw 3 (World3D
  programs layer: common modules own the low sets, the entry module's own
  resources take the first free set — each set belongs to exactly one module).
- Pure UBO blocks sharing one set use sequential registers (`b0`, `b1`, …);
  if a mixed parameters+resources block shares the set, it comes last so its
  members run past the UBOs (see the parameterized-surface test fixture). A
  register on a resource-only block is ignored by Slang — resource-only blocks
  always own their set alone.
- Block members keep their bare field names (`_output`, not
  `_pass._output`) — the shader body and every C# call site address resources
  by name; binding numbers are private physical layout and must not appear in
  caller logic.

Use the actual depth texture and comparison-sampler types. Do not add SPIR-V
rewriters, source regex reflection, implicit structured-buffer counter naming
rules or binding remappers.

When a pass needs an unfiltered raw depth value, declare `DepthTexture2D`, call
`Load`, and bind the framebuffer's native depth attachment with
`SetRenderTextureDepth`. Do not add a color depth mirror or an explicitly
formatted `Texture2D<float>` substitute.

Direct SPIR-V compiler output is mandatory. Do not select a compiler backend in
shader-loading code or introduce a via-GLSL/glslang fallback. On Vulkan,
wgpu-native's SPIR-V passthrough capability submits the validated Slang output
without a Naga import/re-emission round trip. Non-Vulkan backends retain wgpu's
normal target translation path.

## Validation

Before landing shader changes, run:

```text
dotnet build Alco.slnx --no-restore
dotnet test Test/Alco.ShaderCompiler.Test/Alco.ShaderCompiler.Test.csproj --no-build
dotnet test Test/Alco.Rendering.Test/Alco.Rendering.Test.csproj --no-build
dotnet test Test/Alco.World3D.Test/Alco.World3D.Test.csproj --no-build
```

`SlangSourceConventionTest` enforces module headers, file/module naming
(kebab-case files paired with snake_case module names), removes legacy HLSL
and rejects textual includes, a `register(...)` declaration without a set,
`[[vk::binding]]` decorations, and registers outside cbuffer/ConstantBuffer
blocks. `SlangBlockBindingTest` pins the block reflection contract (bare member
names, compiler-assigned order, multi-block sets). Rendering and World3D
validation compile every entry-point module and inspect Slang reflection
headlessly.
