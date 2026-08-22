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

Comments may precede the directive. Module names use valid Slang identifiers;
use lower snake case for new modules and prefix shared modules with their owning
assembly (`alco_rendering_`, `alco_world3d_`). The file resolver accepts dashed
asset filenames, but imports always use the declared module identifier.

Use `import`; `#include` is forbidden. Export only the declarations another
module consumes. Under Slang 2025 visibility is internal by default, so shared
types and functions must be marked `public` deliberately.

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

When a pass needs an unfiltered raw depth value, read the pipeline's
`R32Float` depth-mirror attachment through an explicitly formatted
`[[vk::image_format("r32f")]] Texture2D<float>`. Keep `DepthTexture*` for
comparison sampling and depth semantics. Slang reflection maps the explicit
format to WebGPU's `UnfilterableFloat` sample type.

Direct SPIR-V is the default. Do not select a compiler backend in shader-loading
code; all pinned-toolchain exceptions belong in `SpirvCompat.cs`, must link an
upstream issue, and must have deterministic route tests.

## Validation

Before landing shader changes, run:

```text
dotnet build Alco.slnx --no-restore
dotnet test Test/Alco.ShaderCompiler.Test/Alco.ShaderCompiler.Test.csproj --no-build
dotnet test Test/Alco.Rendering.Test/Alco.Rendering.Test.csproj --no-build
dotnet test Test/Alco.World3D.Test/Alco.World3D.Test.csproj --no-build
```

`SlangSourceConventionTest` enforces module headers, removes legacy HLSL and
rejects textual includes or a `register(...)` declaration without a set.
Rendering and World3D validation compile every entry-point module and inspect
Slang reflection headlessly.
