using System.Numerics;
using System.Runtime.InteropServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class SpriteRenderer : Renderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public Vector4 Color;
    }
    private readonly GPUCommandBuffer _command;
    private readonly GPUDevice _device;
    private readonly Shader _shader;

    private readonly uint _shaderId_camera;
    private readonly uint _shaderId_texture;

    public SpriteRenderer(ICamera camera, Shader shader) : base(camera)
    {
        _device = RendereringContext.Device;
        _command = _device.CreateCommandBuffer();
        _shader = shader;

        _shaderId_camera = _shader.GetResourceId("Camera");
        _shaderId_texture = _shader.GetResourceId("Texture");
    }

    public void Begin(GPUFrameBuffer target)
    {
        _command.Begin();
        _command.SetFrameBuffer(target);
    }

    public void End()
    {
        _command.End();
        _device.Submit(_command);
    }

    public void Draw(Texture2D texture, Matrix4x4 matrix, Vector4 color)
    {
        Constant constant = new Constant
        {
            Model = matrix,
            Color = color
        };

        _command.SetGraphicsPipeline(_shader.Pipeline);

    }

    protected override void Dispose(bool disposing)
    {
        
    }
}