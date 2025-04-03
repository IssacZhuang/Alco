# Alco

Alco is a high-performance game engine designed for optimal CPU and GPU utilization. It's built with .NET 9.0 and provides a comprehensive set of tools and libraries for game development.

## Features

- Cross-platform support (Windows, Linux, macOS)
- Modern graphics API support through WGPU
- Comprehensive rendering pipeline
- High performance math and spatial **implmentation**
- Audio system
- Input/Output handling
- GUI framework
- Asset management
- Auto memory management
- Shader compilation tools

## Cordinate System

The cordinate system and matrix layout are following the DirectX style.

- Left-handed coordinate system
- The matrix layout is row-major

**3D**
- (same as Unreal Engine)
- x+ is forward
- y+ is right
- z+ is up

**2D**
- x+ is right
- y+ is up
- z+ is face into the screen

## Project Structure

- **Src/** - Core engine components
  - **Alco/** - Base library including math, spatial, threading and some utilities
  - **Alco.Engine/** - Main engine implementation
  - **Alco.Graphics/** - Graphics abstraction layer
  - **Alco.Rendering/** - Rendering pipeline
  - **Alco.Audio/** - Audio system
  - **Alco.GUI/** - GUI framework
  - **Alco.IO/** - Input/Output handling
  - **Alco.ShaderCompiler/** - Shader compilation tools
- **Sandbox/** - Example applications demonstrating engine features
- **Test/** - Unit and integration tests
- **Benchmark/** - Performance benchmarks
- **Docs/** - Documentation
- **Tool/** - Development tools
- **Editor/** - Engine editor

## Examples

The `Sandbox/` directory contains numerous examples demonstrating various engine features:
****
- Basic window creation
- Rendering primitives
- Texture handling
- Compute shaders
- 2D transformations
- Instancing
- Text rendering
- Asset loading
- Sprite rendering
- Post-processing effects
- Collision detection
- UI implementation
- Audio playback
- Multi-window support
- And more...

## Getting Started

1. Clone the repository
2. Add reference of Alco.Engine in your own game

## Requirements

- .NET 9.0 SDK
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