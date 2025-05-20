using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Alco.Project.Mcp;

[McpServerPromptType]
public partial class AlcoProjectMcpPrompts
{
    [McpServerPrompt, Description("Basic prompt informing the API that this is the Alco game engine editor")]
    public static string Readme()
    {
        return @"Alco is a high-performance game engine designed for optimal CPU and GPU utilization, built with .NET 9.0. This is the Model Context Protocol (MCP) server integrated into the Alco editor.

Key Features:
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

Coordinate System:
- Left-handed coordinate system with row-major matrix layout
- 3D: x+ is forward, y+ is right, z+ is up
- 2D: x+ is right, y+ is up, z+ is depth

The editor provides comprehensive tools for game development including scene management, asset organization, component-based game objects, visual scripting, code editing, and build/deployment capabilities.";
    }
}
