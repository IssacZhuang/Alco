# Shader Binding Slot Collisions

## Problem

Several declaration macros in `Core.hlsli` each occupy **two** consecutive binding slots: one for the texture and one for its companion sampler. This is easy to overlook when multiple resources share one set, leading to silent binding collisions.

The SPIR-V reflector does **not** detect these collisions. They surface at runtime as:

- `ArgumentException: An item with the same key has already been added` in `ShaderReflectionUtility.MergeBindGroupEntries`
- WebGPU validation error: `Conflicting binding at index N` in `wgpuDeviceCreateBindGroupLayout`

## Macros That Occupy Two Slots

| Macro | Texture slot | Sampler slot |
|---|---|---|
| `DEFINE_TEX2D_SAMPLE_AT(set, bind, name)` | `bind` | `bind + 1` |
| `DEFINE_TEX3D_SAMPLE_AT(set, bind, name)` | `bind` | `bind + 1` |
| `DEFINE_TEX2D_DEPTH_SAMPLE_AT(set, bind, name)` | `bind` | `bind + 1` |

All other macros (`DEFINE_UNIFORM_AT`, `DEFINE_STORAGE_AT`, `DEFINE_TEX2D_READ_AT`, `DEFINE_TEX2D_DEPTH_AT`, `DEFINE_TEX2D_STORAGE_AT`, `DEFINE_TEX3D_READ_AT`, `DEFINE_TEX3D_STORAGE_AT`) occupy exactly **one** slot.

## Rule

When multiple resources share one set, the resource following a texture+sampler pair must start at least at `bind + 2`, not `bind + 1`.

## Examples

**Correct** — storage buffer starts at 3, after the texture (1) and sampler (2):

```hlsl
DEFINE_TEX2D_SAMPLE_AT(1, 1, _albedo);           // bindings 1 + 2
DEFINE_STORAGE_AT(1, 3, MyData, _data);           // binding 3 — OK
```

**Wrong** — storage buffer claims binding 2, which is already taken by the sampler:

```hlsl
DEFINE_TEX2D_SAMPLE_AT(1, 1, _albedo);           // bindings 1 + 2
DEFINE_STORAGE_AT(1, 2, MyData, _data);           // binding 2 — COLLISION!
```

## Real Incidents

Both were found in the PBR deferred pipeline (Sandbox 34):

1. **`DeferredLighting.hlsl`** — `DEFINE_TEX2D_SAMPLE_AT(1, 1, _albedo)` occupies bindings 1–2, but `_pointLights` was declared at binding 2. Fix: moved `_pointLights` to binding 14.

2. **`VoxelInject.hlsl`** — `DEFINE_TEX2D_DEPTH_SAMPLE_AT(0, 3, _shadowMap)` occupies bindings 3–4, but `_pointLights` was declared at binding 4. Fix: moved `_pointLights` to binding 7.

## Checklist for New Shaders

When adding resources to a shared set, lay out all bindings in order and verify that every texture declared with a sample macro is followed by a gap of 2, not 1.
