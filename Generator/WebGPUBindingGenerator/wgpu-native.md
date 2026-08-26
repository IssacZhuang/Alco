# wgpu-native provenance

Alco consumes wgpu-native as prebuilt artifacts. The patch stack (passthrough
feature mapping, DXIL/MSL/metallib C ABI) and the pinned upstream source live
in the [`alco-wgpu-native`](https://github.com/IssacZhuang/alco-wgpu-native)
overlay repository — this project keeps no patch copies.

What Alco consumes from it:

- `Generator/WebGPUBindingGenerator/headers/wgpu.h` — the patched header copy
  the C# binding generator parses. Re-copy it from the overlay repo's patched
  upstream checkout whenever the patch stack changes, then regenerate the
  bindings.
- `Src/Alco.Graphics/runtimes/<rid>/native/` — the built libraries for all
  eight Alco runtime identifiers, plus `wgpu-native-manifest.json` recording
  their release tag and SHA-256 hashes.

## Updating the binaries

The overlay repo's GitHub Actions build every RID on tag push
(`v29.0.1.1-alco.N`). Fetch the release asset and stage it:

```powershell
gh release download v29.0.1.1-alco.3 -R IssacZhuang/alco-wgpu-native -O dist.zip
powershell -ExecutionPolicy Bypass -File <overlay-repo>/scripts/Copy-To-Alco.ps1 `
  -PackagePath dist.zip -AlcoRoot <alco-root>
```

The copy script verifies every file against the manifest before copying.

Passthrough (`PASSTHROUGH_SHADERS`) is how Slang's SPIR-V/DXIL/MSL/metallib
reaches the native backends. Only backends whose adapter exposes it take the
passthrough path; every other combination fails with an explicit
`GraphicsException` instead of an entry-point lookup error. The metallib entry
point additionally requires an `alco.3+` build — the engine probes the loaded
library's export table (`NativeLibrary.TryGetExport`) and stays on MSL when it
is absent.
