# Shader Binding Slot Assignment

Bindings are no longer assigned by hand. All resource declaration macros in
`Core.hlsli` take only the bind group (set) index and expand to
`register(spaceN)` without a register number, so DXC assigns binding numbers
automatically — sequentially per set, in declaration order. A texture declared
with a sample macro and its companion sampler simply take two consecutive
assignments; no binding numbers need to be planned.

```hlsl
DEFINE_TEX2D_SAMPLE(1, _albedo);        // texture + companion sampler
DEFINE_STORAGE(1, MyData, _data);       // next automatic binding — always safe
```

The engine resolves resources by name, never by binding number, so the exact
numbers are irrelevant to C# code. Resources that share one set (e.g. the
per-pass inputs of the PBR deferred pipeline) are just declared one after
another.

## Historical Note

This document previously described "binding slot collisions": the removed
`*_AT` macros required manual binding numbers, and a texture+sampler pair
silently occupied two consecutive slots (`bind` and `bind + 1`), which was
easy to overlook when multiple resources shared one set. The SPIR-V reflector
did not detect the collisions; they surfaced at runtime as
`ArgumentException: An item with the same key has already been added` in
`ShaderReflectionUtility.MergeBindGroupEntries` or as the WebGPU validation
error `Conflicting binding at index N`. Two incidents were found in the PBR
deferred pipeline (Sandbox 34):

1. **`DeferredLighting.hlsl`** — `_albedo` (texture+sampler) occupied bindings 1–2, but `_pointLights` was declared at binding 2.
2. **`VoxelInject.hlsl`** — `_shadowMap` (texture+comparison sampler) occupied bindings 3–4, but `_pointLights` was declared at binding 4.

With compiler-assigned bindings this class of bug cannot occur: a declaration
always receives the next free slot in its set, whatever the declarations
around it reserve.
