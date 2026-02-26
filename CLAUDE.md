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
- **Alco.Rendering/** - Rendering pipeline
- **Alco.Audio/** - Audio system
- **Alco.GUI/** - GUI framework
- **Alco.IO/** - Input/Output handling
- **Alco.ShaderCompiler/** - Shader compilation tools
- **Alco.LLM/** - Large Language Model integration system using Microsoft Semantic Kernel

### Other Directories
- **Sandbox/** - Example applications demonstrating engine features
- **Test/** - Unit and integration tests
- **Benchmark/** - Performance benchmarks
- **Docs/** - Documentation
- **Tool/** - Development tools
- **Editor/** - Engine editor

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

## Interaction Guidelines
- When writing code, if there are any ambiguities or unclear requirements, always ask for the user's intent and wait for confirmation before proceeding. Do not guess or make assumptions.

## Shader Guidelines
- Read `Src/Alco.Engine/Assets/Shaders/Libs/Core.hlsli` before editing shaders.
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
- Microsoft.SemanticKernel (v1.71.0) - LLM integration framework

## Development Requirements
- .NET 10.0 SDK
- Visual Studio 2022 or compatible IDE
