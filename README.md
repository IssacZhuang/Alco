# Alco

Alco is a high-performance game engine designed for optimal CPU and GPU utilization. It's built with .NET 10.0 and provides a comprehensive set of tools and libraries for game development.

> **Note:** Alco is currently developed as a custom engine for an independent game project. Pull requests and issues are temporarily not accepted while the engine remains tightly coupled to the game project.

## Features

- Windows only; Linux and macOS are untested
- Modern graphics API support through WGPU
- Comprehensive rendering pipeline
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

## Third-Party Libraries

- [Silk.NET](https://github.com/dotnet/Silk.NET): OpenAL audio and SPIR-V reflection
- [Alimer.Bindings.WebGPU](https://github.com/amerkoleci/Alimer.Bindings.WebGPU): WebGPU API bindings
- [wgpu-native](https://github.com/gfx-rs/wgpu-native): WebGPU native implementation
- [DirectX Compiler](https://github.com/microsoft/DirectXShaderCompiler): HLSL shader compilation
- [System.IO.Hashing](https://www.nuget.org/packages/System.IO.Hashing): High-performance hashing
- [StbSharp](https://github.com/StbSharp): Image and font processing (embedded as source code and modified)
- [ImGui](https://github.com/ocornut/imgui): Immediate Mode Graphical User Interface
- [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET): ImGui binding for .NET (embedded as source code and modified)
- [ImGuizmo](https://github.com/CedricGuillemet/ImGuizmo): Gizmo manipulation widgets for ImGui

All external libraries are used under their respective licenses, primarily MIT License.

## Notice

- Alco is purpose-built for an independent game project and is primarily driven by its specific requirements.
- **Pull requests and issues are temporarily not accepted** while the engine remains tightly coupled to the game project.
- APIs, architecture, and features may change frequently without notice or backward compatibility guarantees.
- Feel free to fork and modify for your own use under the terms of the MIT License.

## License

[MIT License](LICENSE)