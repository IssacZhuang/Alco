# Slang shader binding assignment

The runtime shader contract is name-based. Slang reflection produces each
resource's descriptor set, binding, kind, visibility, texture sample type and
storage format; C# resolves the resource by name through
`ShaderReflectionInfo`. Callers must never treat a binding number as a public
resource identifier.

Shader sources declare the physical Vulkan layout explicitly:

```slang
[[vk::binding(0, 0)]] ConstantBuffer<FrameData> _frame;
[[vk::binding(0, 1)]] Texture2D<float4> _albedo;
[[vk::binding(1, 1)]] SamplerState _albedoSampler;
```

The two arguments are `(binding, set)`. Sets follow the engine frequency
layout:

- set 0: frame resources;
- set 1: pass resources;
- set 2: material resources;
- set 3: draw resources.

Bindings must be unique inside a set and contiguous from zero. Keep a texture
and its sampler as separate reflected resources; the runtime pairs them by the
texture resource entry, not by arithmetic performed by callers. Depth textures
use Slang depth texture types and `SamplerComparisonState`, so neither source
regexes nor SPIR-V patching participate in layout construction.

`ShaderReflectionUtility.ValidateBindGroupLayouts` rejects non-contiguous sets,
duplicate bindings and layouts beyond the device limit. The Slang validation
tests compile every module and verify the reflected layout before a shader can
reach a GPU pipeline.

## Historical note

The removed DXC pipeline used `DEFINE_*` macros, `register(spaceN)` automatic
assignment, source-level sampler suffix conventions and a custom SPIR-V
reflector. Earlier `_AT` macros could accidentally overlap a texture's sampler
binding with the next resource. Explicit Slang declarations plus compiler
reflection make those collisions visible during headless validation rather
than at WebGPU pipeline creation.
