using Vocore.Graphics;

namespace Vocore.Rendering;

public class BlitRenderer : AutoDisposable
{

    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
    private readonly uint _shaderId_Texture;
    //external resources 
    private readonly Mesh _fullScreenQuad;
    private GPUPipeline? _pipelineBlit;
    private GPURenderPass? _targetRenderPass;

    private readonly Shader _shaderBlit;

    internal BlitRenderer(RenderingSystem renderingSystem, Shader shaderBlit)
    {
        _device = renderingSystem.GraphicsDevice;
        _fullScreenQuad = renderingSystem.MeshFullScreen;
        _command = _device.CreateCommandBuffer(new CommandBufferDescriptor("blit"));
        _shaderBlit = shaderBlit;
        _shaderId_Texture = shaderBlit.GetResourceId("_texture");
    }

    public void Blit(Texture2D from, GPUFrameBuffer to)
    {
        if (_targetRenderPass != to.RenderPass)
        {
            _targetRenderPass = to.RenderPass;
            _pipelineBlit = _shaderBlit.GetPipelineVariant(_targetRenderPass);
        }

        _command.Begin();
        _command.SetFrameBuffer(to);
        _command.SetGraphicsPipeline(_pipelineBlit!);
        _command.SetVertexBuffer(0, _fullScreenQuad.VertexBuffer);
        _command.SetIndexBuffer(_fullScreenQuad.IndexBuffer, _fullScreenQuad.IndexFormat);
        _command.SetGraphicsResources(_shaderId_Texture, from.EntrySample);
        _command.DrawIndexed(_fullScreenQuad.IndexCount, 1, 0, 0, 0);
        _command.End();
        _device.Submit(_command);
    }
    public void Blit(RenderTexture from, GPUFrameBuffer to)
    {
        if (_targetRenderPass != to.RenderPass)
        {
            _targetRenderPass = to.RenderPass;
            _pipelineBlit = _shaderBlit.GetPipelineVariant(_targetRenderPass);
        }

        _command.Begin();
        _command.SetFrameBuffer(to);
        _command.SetGraphicsPipeline(_pipelineBlit!);
        _command.SetVertexBuffer(0, _fullScreenQuad.VertexBuffer);
        _command.SetIndexBuffer(_fullScreenQuad.IndexBuffer, _fullScreenQuad.IndexFormat);
        _command.SetGraphicsResources(_shaderId_Texture, from.EntriesColorSample[0]);
        _command.DrawIndexed(_fullScreenQuad.IndexCount, 1, 0, 0, 0);
        _command.End();
        _device.Submit(_command);
    }

    public void Blit(Texture2D from, RenderTexture to)
    {
        Blit(from, to.FrameBuffer);
    }

    public void Blit(RenderTexture from, RenderTexture to)
    {
        Blit(from, to.FrameBuffer);
    }


    protected override void Dispose(bool disposing)
    {
        //dispose previous managed resources
        _command.Dispose();
    }
}