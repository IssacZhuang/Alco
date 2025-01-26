namespace Alco.Graphics;

public enum GraphicsBackend
{
    None = 0,
    Auto = 1,
    WebGPU = 2,
    Vulkan = 3,
    D3D11 = 4,
    D3D12 = 5,
    Metal = 6,
    OpenGL = 7,
    OpenGLES = 8,
}