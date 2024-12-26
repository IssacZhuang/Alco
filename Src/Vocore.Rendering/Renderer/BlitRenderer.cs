using Vocore.Graphics;

namespace Vocore.Rendering;

public class BlitRenderer : AutoDisposable
{

    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUCommandBuffer _command;

    //external resources 
    private readonly Mesh _fullScreenQuad;
    private readonly Shader _shaderBlit;
    private GraphicsPipelineContext _pipelineInfo;
    private uint _shaderId_Texture;

    internal BlitRenderer(RenderingSystem renderingSystem, Shader shaderBlit)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _fullScreenQuad = renderingSystem.MeshFullScreen;
        _command = _device.CreateCommandBuffer(new CommandBufferDescriptor("blit"));
        _shaderBlit = shaderBlit;
        _pipelineInfo = _shaderBlit.GetGraphicsPipeline(
            renderingSystem.PrefferedSDRPass,
            DepthStencilState.Default,
            BlendState.AlphaBlend
            );
        _shaderId_Texture = _pipelineInfo.GetResourceId("_texture");
    }

    public void Blit(Texture2D from, GPUFrameBuffer to)
    {
        if (_shaderBlit.TryUpdatePipelineContext(ref _pipelineInfo, to.RenderPass))
        {
            _shaderId_Texture = _pipelineInfo.GetResourceId("_texture");
        }

        _command.Begin();
        _command.SetFrameBuffer(to);
        _command.SetGraphicsPipeline(_pipelineInfo);
        _command.SetVertexBuffer(0, _fullScreenQuad.VertexBuffer);
        _command.SetIndexBuffer(_fullScreenQuad.IndexBuffer, _fullScreenQuad.IndexFormat);
        _command.SetGraphicsResources(_shaderId_Texture, from.EntrySample);
        _command.DrawIndexed(_fullScreenQuad.IndexCount, 1, 0, 0, 0);
        _command.End();
        _renderingSystem.ScheduleCommandBuffer(_command);
    }
    public void Blit(RenderTexture from, GPUFrameBuffer to)
    {
        if (_shaderBlit.TryUpdatePipelineContext(ref _pipelineInfo, to.RenderPass))
        {
            _shaderId_Texture = _pipelineInfo.GetResourceId("_texture");
        }

        _command.Begin();
        _command.SetFrameBuffer(to);
        _command.SetGraphicsPipeline(_pipelineInfo);
        _command.SetVertexBuffer(0, _fullScreenQuad.VertexBuffer);
        _command.SetIndexBuffer(_fullScreenQuad.IndexBuffer, _fullScreenQuad.IndexFormat);
        _command.SetGraphicsResources(_shaderId_Texture, from.EntriesColorSample[0]);
        _command.DrawIndexed(_fullScreenQuad.IndexCount, 1, 0, 0, 0);
        _command.End();
        _renderingSystem.ScheduleCommandBuffer(_command);
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