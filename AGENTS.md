# Alco Engine Coding Standards

## Project Context
- High-performance game engine for optimal CPU and GPU utilization
- Built with .NET 10.0
- Cross-platform support (Windows, Linux, macOS)

## Project Structure

### Main Source (Src/)
- **Alco/** - Base library including math, spatial, threading, and utilities
- **Alco.Engine/** - Main engine implementation
- **Alco.Graphics/** - Graphics abstraction layer
- **Alco.Rendering/** - Rendering pipeline (render graph, render pipeline framework, shared GPU resource facades) and its built-in shaders (Assets/Shaders)
- **Alco.World3D/** - 3D PBR rendering module (deferred render nodes, scene environment, preset factory, PBR shaders); references only Alco.Rendering and is consumed on demand — neither Alco.Engine nor Alco.Rendering references it
- **Alco.Particles/** - GPU particle system (particle effect assets with emitter groups, slang behavior modules composed through interface specialization, visuals from .amat material assets whose slang surface composes into the render pass templates — groups derive their own texture over the material's "texture" slot, shared buffer pools, indirect-instanced rendering, optional per-group over-life color-gradient/size-curve lookup textures baked CPU-side and sampled by age in the render vertex shader, velocity-stretched billboards via flag bits in EmitterParams); references only Alco.Rendering and Alco.IO, consumed on demand (sandboxes 36/37)
- **Alco.Audio/** - Audio system
- **Alco.GUI/** - GUI framework
- **Alco.IO/** - Input/Output handling
- **Alco.ShaderCompiler/** - Shader compilation tools
- **Alco.AgentControlProtocol/** - External agent control plane: tool registry (attribute-discovered agent functions with main-thread marshaling), localhost HTTP API server (Kestrel), built-in ExecuteScript (Roslyn C# scripting) and CaptureScreenshot tools; depends only on Alco.Engine — referencing this project gives a game full external AI-agent control
- **Alco.LLM/** - In-game LLM agent framework (LLMAgent/LLMSession over Microsoft.Extensions.AI with OpenAI/Anthropic/Gemini providers); depends on Alco.AgentControlProtocol and reuses its tool registry

### Other Directories
- **Sandbox/** - Example applications demonstrating engine features
- **Test/** - Unit and integration tests
- **Benchmark/** - Performance benchmarks
- **Docs/** - Documentation
- **Tool/** - Development tools
- **Editor/** - Editor engine (Alco.Editor): the editor is a framework, not an app — hosts documents, preview viewports and panels, and exposes extensibility registries (`Alco.Editor/Extensibility/`: IEditorModule, document/preview-pipeline/panel/menu/asset-template registries, interface-keyed EditorServices) so a game project composes its own editor app; Alco.Editor.App is the vanilla shell for engine development

## Coordinate System
- Left-handed, row-major matrices (following Unreal Engine style)

**3D**
- X+ is forward
- Y+ is right
- Z+ is up

**2D**
- X+ is right
- Y+ is up
- Z+ is into the screen (depth)

## Documentation Requirements
- Always add comments for all public classes, methods, and properties after editing. Comments for private members are not required.
- All documentation comments must be written in English.
- Use standard XML documentation tags (`<summary>`, `<param>`, `<returns>`, etc.).

## Performance Guidelines
- Prefer `for` loops with integer indexing over `foreach` when performance is critical or when index access is needed.
- Use `Span<T>` or `ReadOnlySpan<T>` for method parameters when accepting collections, especially for performance-sensitive code.

## Object Initialization Guidelines
- Follow RAII (Resource Acquisition Is Initialization) principles when initializing objects.
- All non-nullable members should be fully initialized in the constructor.
- Nullable resources (e.g., late-loaded assets, lazily initialized dependencies) are exceptions and may be initialized after construction.
- Prefer constructor injection over two-phase initialization (e.g., avoid separate `Initialize()` methods).
- Objects should be ready for use immediately after construction without requiring additional setup calls.
- For mutable value types or mutable references, recommend using getter/setter properties instead of public fields, and they don't need to be passed in the constructor.

## Render Node Construction Guidelines
- Render node constructors take `(services..., in Descriptor)`: service-type dependencies
  (the rendering system, graph, chain, graph resources, sibling nodes) are explicit
  constructor parameters; everything constructible from data (shader references, tunable
  scalars) lives in a nested `readonly struct Descriptor` on the node class with
  `required` properties for shaders and property initializers for defaults. A descriptor
  never holds services — it is pure (serializable) data.
- `RenderNodeFactory` classes (`RGNodeFactory_*`) load from `.rnfact` jsonc assets
  (camelCase, `$type` = CLR full name); they hold the node's descriptor plus factory-level
  flags, resolve shader module names through the shader system at load time, and create
  the node in `Create(RenderNodeFactoryContext)`. Runtime overrides go to the created
  node's properties — loaded factories are shared cached assets and must not be mutated.
  Pipeline-shape dependencies come from the context's `RenderNodeFactoryServices`
  type-keyed blackboard, never from feature-specific context members.

## Interaction Guidelines
- When writing code, if there are any ambiguities or unclear requirements, always ask for the user's intent and wait for confirmation before proceeding. Do not guess or make assumptions.

## Shader Guidelines
- Shaders are Slang modules (see `docs/SlangCodingStandard.md`); read the material/surface contract `Src/Alco.World3D/Assets/Shaders/Libs/alco-world3d-surface.slang` and `docs/MaterialSystem.md` before editing shaders (PBR-specific shader libs live under `Src/Alco.World3D/Assets/Shaders/`).
- All comments in shaders must be English.
- Run `dotnet test --filter "ValidateShader"` to test shaders after editing.

## Build Guidelines
- Run `dotnet build` after editing C# code to ensure compilation succeeds.

## Third-Party Dependencies
- Silk.NET (v2.22.0) - OpenAL audio and SPIR-V reflection
- Alimer.Bindings.WebGPU (v1.5.0) - WebGPU API bindings
- wgpu-native - WebGPU native implementation
- DirectX Compiler - HLSL shader compilation
- System.IO.Hashing (v9.0.0) - High-performance hashing
- StbSharp - Image and font processing (embedded as source code and modified)
- ImGui - Immediate Mode Graphical User Interface (embedded as source code and modified)
- ImGui.NET - ImGui binding for .NET (embedded as source code and modified)
- Microsoft.Extensions.AI (v10.6.0) - LLM integration abstraction (Alco.LLM)

## Development Requirements
- .NET 10.0 SDK
- Visual Studio 2022 or compatible IDE
