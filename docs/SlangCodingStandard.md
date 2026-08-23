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

Prefer generic type/value parameters and link-time specialization for new
variants. Preprocessor defines remain only for migrated compatibility variants;
do not introduce a new `#if` permutation when a generic specialization can
express it. ShaderSystem specialization arguments are part of the program cache
identity.

## Resources and bindings

Use real Slang resource types (`Texture*`, `SamplerState`,
`SamplerComparisonState`, structured buffers and storage textures). New code
must not add `DEFINE_*`, `SLOT`, sampler-token-concatenation or depth-marker
macros.

Declare both binding and set with `[[vk::binding(binding, set)]]`. Sets are
frequency grouped: frame 0, pass 1, material 2 and draw 3. Bindings are
contiguous from zero within each used set. C# resolves resources by reflected
name; binding numbers are private physical layout and must not appear in caller
logic.

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
and rejects textual includes or a `register(...)` declaration without a set.
Rendering and World3D validation compile every entry-point module and inspect
Slang reflection headlessly.
