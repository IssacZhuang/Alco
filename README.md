# Alco

Alco is a high-performance game engine designed for optimal CPU and GPU utilization. It's built with .NET 10.0 and provides a comprehensive set of tools and libraries for game development.

## Features

- Cross-platform support (Windows, Linux, macOS)
- Modern graphics API support through WGPU
- Comprehensive rendering pipeline
- High performance math and spatial implementation
- Audio system
- Input/Output handling
- GUI framework
- Asset management
- Auto memory management
- Shader compilation tools

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

1. Clone the repository as git submodule
2. Add reference of Alco.Engine in your own game

## Requirements

- .NET 10.0 SDK
- Visual Studio 2022 or compatible IDE

## Third-Party Libraries

- [Silk.NET](https://github.com/dotnet/Silk.NET) (v2.22.0): OpenAL audio and SPIR-V reflection
- [Alimer.Bindings.WebGPU](https://github.com/amerkoleci/Alimer.Bindings.WebGPU) (v1.5.0): WebGPU API bindings
- [wgpu-native](https://github.com/gfx-rs/wgpu-native): WebGPU native implementation
- [DirectX Compiler](https://github.com/microsoft/DirectXShaderCompiler): HLSL shader compilation
- [System.IO.Hashing](https://www.nuget.org/packages/System.IO.Hashing) (v9.0.0): High-performance hashing
- [StbSharp](https://github.com/StbSharp): Image and font processing (embedded as source code and modified)
- [ImGui](https://github.com/ocornut/imgui): Immediate Mode Graphical User Interface
- [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET): ImGui binding for .NET (embedded as source code and modified)

All external libraries are used under their respective licenses, primarily MIT License.

## License

[MIT License](LICENSE)