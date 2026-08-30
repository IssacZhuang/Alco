# IInspector — UI-Agnostic Parameter Editing Contract

## Motivation

The engine will get an editor for tuning engine assets (particle effects, materials,
render node factories, ...). The editor must own the UI (currently ImGui; later the
editor's own toolkit), while the assets and engine modules must not know anything
about editing or UI toolkits — they live in modules that sit *below* any UI layer in
the dependency graph:

```
Alco.ImGUI  ──▶  Alco.Engine  ──▶  Alco.Rendering / Alco.Particles / Alco.IO / ...
     │                                     │
     └────────── implements ───────▶  Alco (base module, zero dependencies)
                                            ▲  IInspector / IInspectable / NullInspector
```

The contract is the only thing engine code depends on. `Alco.ImGUI` (or a future
editor project) implements it — no new dependencies, no cycles.

No engine asset implements `IInspectable` yet; wire individual assets with the
pattern below when needed — the contract, backends and test tooling are already in
place for it.

## Files

| File | Content |
|---|---|
| `Src/Alco/Inspector/IInspector.cs` | The widget contract (+ default `Combo<T>` enum helper) |
| `Src/Alco/Inspector/IInspectable.cs` | "This object exposes parameters" marker + `Inspect(IInspector)` |
| `Src/Alco/Inspector/NullInspector.cs` | Shared no-op backend (headless tools, tests) |
| `Src/Alco.ImGUI/ImGuiInspector.cs` | Reference ImGui backend |
| `Test/TestFramework/InspectorRecorder.cs` | Scripted recording backend for tests |

## Contract rules

1. **Immediate mode.** Call the widgets every frame while the parameter rows should
   stay visible. No retained widget tree, no layout API.
2. **`ref` in, `bool` out.** Edits happen in place through the `ref` parameter. A
   widget returns `true` only when the user edited the value during this frame; the
   `ref` value already holds the new value then. No allocations on the caller side.
3. **Label = identity.** The label doubles as the stable widget identifier; keep it
   unique within one panel (ImGui id semantics).
4. **Text is span-based.** All text parameters are `ReadOnlySpan<char>` (combo items:
   `ReadOnlySpan<string>`). Labels are usually literals, so the per-frame path stays
   allocation-free; implementations must not store them — widgets draw immediately.
5. **Naming.** Scalar drag widgets are `Drag*` (`DragFloat`, `DragInt`), sliders are
   `Slider*`; multi-component vector editors are `Edit*` (`EditFloat2/3/4`,
   `EditInt2/3/4`), matching the `EditTransform2D/3D` precedent of the ImGUI module.
6. **Bounds.** Drag widgets default to unbounded (`±∞` for floats, `int.MinValue/MaxValue`
   for ints); a bound applies only when `min < max` and clamps every component of
   vector widgets. Sliders require a range.
7. **Call through the interface.** Optional parameter defaults and the default
   `Combo<T>` body live on the interface — they only apply when the static call type
   is `IInspector` (which is also how default interface methods dispatch).

## API surface

| Method | Value type | ImGui counterpart |
|---|---|---|
| `Text`, `Separator`, `CollapsingHeader` | layout | `Text`, `Separator`, `CollapsingHeader` |
| `DragFloat`, `DragInt` | `float`, `int` (scalar drags) | `DragFloat`, `DragInt` |
| `EditFloat2/3/4` | `Vector2/3/4` | `DragFloat2/3/4` |
| `EditInt2/3/4` | `int2/3/4` | `DragInt2/3/4` |
| `SliderFloat`, `SliderInt` | `float`, `int` | `SliderFloat`, `SliderInt` |
| `Checkbox` | `bool` | `Checkbox` |
| `InputText` | `string` (with `maxLength`) | `InputText` |
| `Combo` | `int` index + `ReadOnlySpan<string>` items | `Combo` |
| `Combo<T>` | any `T : struct, Enum` | (built on `Combo`) |
| `ColorEdit3` | `Vector3` | `ColorEdit3` |
| `ColorEdit4(hdr)` | `Vector4` | `ColorEdit4` (+`ImGuiColorEditFlags.HDR`) |

Design notes:

- Float vectors are `System.Numerics` (the engine-wide standard); int vectors are the
  base module's `int2/3/4` — the binding's `DragInt2/3/4` take `ref int` (a pointer to
  N contiguous ints), which `ImGuiInspector` stages through a `stackalloc` span.
  The contract normalizes that quirk: engine code always passes a real vector type.
- `Combo<T>` is a *default interface method*: it maps the enum to its declaration-order
  names/values (cached per enum type) and routes through the abstract `Combo`. Every
  backend gets enum support for free by implementing the int-based `Combo`.
- The ImGui binding exposes `ReadOnlySpan<char>` overloads for every widget, so the
  reference backend forwards labels without allocating. One exception: the native
  combo consumes a `char*[]`, so `ImGuiInspector.Combo` materializes the item span
  into an array (combos are rare per frame).
- No format strings, flags or popup parameters on purpose — the contract stays at the
  "edit a parameter" level. Widget styling belongs to the backend.

## Exposing parameters from engine code

Implement `IInspectable` on the object (there is no common asset base class, so hosts
discover editability by type check — `if (obj is IInspectable inspectable) inspectable.Inspect(inspector);`):

```csharp
public sealed class BloomSettings : IInspectable
{
    public float Threshold { get; set; } = 1.0f;
    public Vector3 Tint;

    public void Inspect(IInspector inspector)
    {
        // Fields: pass by ref directly, zero copies.
        inspector.EditFloat3("Tint", ref Tint, 0.01f, 0f, 4f);

        // Properties: copy -> widget -> write back on edit.
        float threshold = Threshold;
        if (inspector.DragFloat("Threshold", ref threshold, 0.01f, 0f, 4f))
        {
            Threshold = threshold;
        }
    }
}
```

Groups/children are inspected the same way an editor host would discover them:
iterate the children and forward to the ones implementing `IInspectable`.

`NullInspector.Instance` runs the same code paths with no UI — useful for headless
validation (and it is what keeps `Inspect` implementations trivially testable).

## Implementing a backend

A backend implements `IInspector` and maps each widget onto its toolkit; see
`Src/Alco.ImGUI/ImGuiInspector.cs` (~130 lines). Requirements:

- honor the `bool` = edited semantics and the `ref` write-through,
- treat the label as the widget id (stable from frame to frame) and draw immediately
  (spans are only valid for the duration of the call),
- apply the clamp rule (`min < max`, every component for vectors),
- map `hdr` to the toolkit's HDR color mode.

The future editor can wrap its own widgets the same way, and needs no engine-side
changes: engine modules only ever see the interface.

## Testing

- `TestFramework.InspectorRecorder` records every call (`"Widget:label"`) and applies
  label-keyed scripted edits (`.Edit("Duration (s)", 2.5f)`), so `Inspect`
  implementations can be tested without any UI. Combo scripting uses the positional
  index (that is what the int-based `Combo` receives).
- `NullInspector` asserts the no-edit path leaves state untouched.
- Contract tests: `Test/Alco.Test/Inspector/TestInspector.cs` (default `Combo<T>`,
  `CollapsingHeader` gating, `NullInspector`).

## Extending the contract

To add a widget (e.g. `SliderFloat3`): add the abstract method with English XML docs,
span-based text parameters and sensible defaults to `IInspector`, implement it in
every backend (`ImGuiInspector`, `NullInspector`, `InspectorRecorder`), update the
API table above, and add a contract test in `Test/Alco.Test/Inspector/TestInspector.cs`.
