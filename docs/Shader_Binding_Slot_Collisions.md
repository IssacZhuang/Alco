# Slang shader binding assignment

The runtime shader contract is name-based. Slang reflection produces each
resource's descriptor set, binding, kind, visibility, texture sample type and
storage format; C# resolves the resource by name through
`ShaderReflection`. Callers must never treat a binding number as a public
resource identifier.

Shader sources declare only **which set they own**; Slang assigns member
bindings in declaration order. Every set is one cbuffer block:

```slang
cbuffer _pass : register(b0, space1)
{
    Texture2D<float4> _albedo;   // binding 0 in the set
    SamplerState _albedoSampler; // binding 1
};
```

Sets follow the engine frequency layout:

- set 0: frame resources;
- set 1: pass resources;
- set 2: material resources;
- set 3: draw resources.

Each set belongs to exactly one module; a program composed of several modules
allocates the first free set to each importing module's own block. A block
with uniform data emits its buffer at the block's register and members
continue after it; a resource-only block emits no buffer and its members take
the set's bindings from zero. Blocks sharing one set use sequential registers
(`b0`, `b1`, …) with any mixed parameters+resources block last. Keep a texture
and its sampler as separate reflected block members; the runtime pairs them by
the texture resource entry, not by arithmetic performed by callers. Depth
textures use Slang depth texture types and `SamplerComparisonState`, so
neither source regexes nor SPIR-V patching participate in layout construction.

`ShaderReflectionUtility.ValidateBindGroupLayouts` rejects non-contiguous sets,
duplicate bindings and layouts beyond the device limit; the reflection reader
additionally rejects duplicate resource names across sets. `SlangBlockBindingTest`
pins the block reflection contract and `SlangSourceConventionTest` rejects
`[[vk::binding]]` in sources. The Slang validation tests compile every module
and verify the reflected layout before a shader can reach a GPU pipeline.

## Historical note

The removed DXC pipeline used `DEFINE_*` macros, `register(spaceN)` automatic
assignment, source-level sampler suffix conventions and a custom SPIR-V
reflector. Earlier `_AT` macros could accidentally overlap a texture's sampler
binding with the next resource. The first Slang migration answered with explicit
`[[vk::binding(binding, set)]]` pairs on every resource, which made collisions
visible during headless validation but pinned every binding number in source.
The set-scoped block convention (Slang 2026.16) removed that last coupling:
only the set is written, bindings are compiler-owned, and inserting a member
shifts bindings without touching any C# call site.
