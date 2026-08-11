# Alco

Alco is a high-performance game engine designed for optimal CPU and GPU utilization. It's built with .NET 10.0 and provides a comprehensive set of tools and libraries for game development.

> **Note:** Alco is currently developed as a custom engine for an independent game project. Pull requests and issues are temporarily not accepted while the engine remains tightly coupled to the game project.

## Features

- Windows only; Linux and macOS are untested
- Modern graphics API support through WGPU
- Comprehensive rendering pipeline
- Cascaded Radiance Cache Global Illumination (persistent off-screen cache and iterative diffuse bounce)
- Voxel Cone Tracing Global Illumination (SVOGI-style sparse voxel GI)
- High performance math and spatial implementation
- Audio system (via OpenAL Soft)
- Input/Output handling
- GUI framework
- Asset management (both async and concurrent loading are supported)
- Auto memory management
- Shader compilation tools with HLSL support (via DirectX Compiler)

## Project Structure

- **Src/** - Engine source code
- **Sandbox/** - Example applications
- **Test/** - Unit and integration tests
- **Benchmark/** - Performance benchmarks
- **Docs/** - Documentation
- **Tool/** - Development tools
- **Editor/** - Engine editor

See [CLAUDE.md](CLAUDE.md) for detailed project structure, coordinate system, and coding standards.

## Examples

The `Sandbox/` directory contains numerous examples demonstrating various engine features including rendering, UI, audio, asset loading, and more.

## Getting Started

1. Clone the repository
2. Add a project reference to `Alco.Engine` (and other required modules) in your own game

You can also include it as a git submodule if preferred.

## Requirements

- .NET 10.0 SDK
- Visual Studio 2022 or compatible IDE

## Credits

### Third-Party Libraries

- [Silk.NET](https://github.com/dotnet/Silk.NET): OpenAL audio
- [Alimer.Bindings.WebGPU](https://github.com/amerkoleci/Alimer.Bindings.WebGPU): WebGPU API bindings (embedded as source code and modified)
- [wgpu-native](https://github.com/gfx-rs/wgpu-native): WebGPU native implementation
- [DirectX Compiler](https://github.com/microsoft/DirectXShaderCompiler): HLSL shader compilation
- [System.IO.Hashing](https://www.nuget.org/packages/System.IO.Hashing): High-performance hashing
- [StbSharp](https://github.com/StbSharp): Image and font processing (embedded as source code and modified)
- [ImGui](https://github.com/ocornut/imgui): Immediate Mode Graphical User Interface
- [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET): ImGui binding for .NET (embedded as source code and modified)
- [ImGuizmo](https://github.com/CedricGuillemet/ImGuizmo): Algorithm reference for the built-in C# manipulation gizmo (Src/Alco.ImGUI/Gizmo, no longer a runtime dependency)

All external libraries are used under their respective licenses, primarily MIT License.

### Algorithm References

- The Radiance Cache GI system combines world-space cache and temporal-update ideas from [DDGI](https://jcgt.org/published/0008/02/01/) with the screen-near/world-far split described by [Lumen](https://www.realtimerendering.com/advances/s2022/). See [docs/RadianceCacheGI.md](docs/RadianceCacheGI.md).
- The Global Illumination system (Voxel Cone Tracing / SVOGI) references the algorithm design and parameter choices of CRYENGINE 5's SVOTI (Sparse Voxel Octree Total Illumination).
- The SPIR-V reflection and bytecode parsing module (`Src/Alco.Graphics/Spirv/`) is a pure-managed C# reimplementation referencing [SPIRV-Reflect](https://github.com/KhronosGroup/SPIRV-Reflect) by Khronos Group.

## Notice

- Alco is purpose-built for an independent game project and is primarily driven by its specific requirements.
- **Pull requests and issues are temporarily not accepted** while the engine remains tightly coupled to the game project.
- APIs, architecture, and features may change frequently without notice or backward compatibility guarantees.
- Feel free to fork and modify for your own use under the terms of the MIT License.

## License

[MIT License](LICENSE)
