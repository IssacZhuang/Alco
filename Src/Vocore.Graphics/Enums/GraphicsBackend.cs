namespace Vocore.Graphics;

public enum GraphicsBackend
{
    None = 0,
    WebGPU = 1,
    Vulkan = 2,
    D3D11 = 3,
    D3D12 = 4,
    Metal = 5,
    OpenGL = 6,
    OpenGLES = 7,
}