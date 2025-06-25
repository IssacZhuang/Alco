using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

public class Bloom : PostProcess
{
    private struct ClampShaderData
    {
        public Vector2 InvFrameSize;
        public float Threshold;
    }

    public const string ShaderId_texture = "_texture";
    public const string ShaderId_data = "_data";
    public const string ShaderId_previousTexture = "_previousTexture";
    public const string ShaderId_currentTexture = "_currentTexture";

    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
    private readonly GPUAttachmentLayout _backBufferPass;
    private readonly RenderingSystem _renderingSystem;

    private readonly Shader _blitShader;
    private GraphicsPipelineContext _blitPipelineInfo;
    private uint _blitShaderId_texture;

    protected RenderTexture? _input;

    private readonly GraphicsValueBuffer<ClampShaderData> _clampShaderData;

    //for clamp
    private readonly Shader _clampShader;
    private GraphicsPipelineContext _clampPipelineInfo;
    private readonly uint _clampShaderId_texture;
    private readonly uint _clampShaderId_data;

    private readonly Shader _downSampleShader;
    private GraphicsPipelineContext _downSamplePipelineInfo;
    private uint _downSampleShaderId_texture;


    private readonly uint _targetDownSampleHeight;

    private RenderTexture[]? _downSampleTextures;

    private readonly Shader _upSampleShader;
    private GraphicsPipelineContext _upSamplePipelineInfo;
    private readonly uint _upSampleShaderId_previousTexture;
    private readonly uint _upSampleShaderId_currentTexture;

    private RenderTexture[]? _upSampleTextures;
    internal Bloom(RenderingSystem _system, Shader blitShader, Shader clampShader, Shader downSampleShader, Shader upSampleShader, uint targetDownSampleHeight) : base(_system, blitShader)
    {
        _device = _system.GraphicsDevice;
        _renderingSystem = _system;
        _targetDownSampleHeight = targetDownSampleHeight;

        _backBufferPass = _system.PrefferedHDRPassWithoutDepth;

        _blitShader = blitShader;
        _blitPipelineInfo = GraphicsPipelineContext.Default with
        {
            DepthStencil = DepthStencilState.Default,
            BlendState = BlendState.Additive
        };


        _clampShader = clampShader;
        _clampPipelineInfo = GraphicsPipelineContext.Default;
        _clampShader.TryUpdatePipelineContext(ref _clampPipelineInfo, _backBufferPass);
        _clampShaderData = _system.CreateGraphicsValueBuffer<ClampShaderData>("bloom_threshold");
        _clampShaderId_texture = _clampPipelineInfo.GetResourceId(ShaderId_texture);
        _clampShaderId_data = _clampPipelineInfo.GetResourceId(ShaderId_data);

        _downSampleShader = downSampleShader;
        _downSamplePipelineInfo = GraphicsPipelineContext.Default;
        _downSampleShader.TryUpdatePipelineContext(ref _downSamplePipelineInfo, _backBufferPass);
        _downSampleShaderId_texture = _downSamplePipelineInfo.GetResourceId(ShaderId_texture);

        _upSampleShader = upSampleShader;
        _upSamplePipelineInfo = GraphicsPipelineContext.Default;
        _upSampleShader.TryUpdatePipelineContext(ref _upSamplePipelineInfo, _backBufferPass);
        _upSampleShaderId_previousTexture = _upSamplePipelineInfo.GetResourceId(ShaderId_previousTexture);
        _upSampleShaderId_currentTexture = _upSamplePipelineInfo.GetResourceId(ShaderId_currentTexture);

        _command = _device.CreateCommandBuffer();
    }

    public override void SetInput(RenderTexture input)
    {
        base.SetInput(input);

        _input = input;

        TryDisposeFrames();

        _clampShaderData.Value = new ClampShaderData
        {
            InvFrameSize = new Vector2(1f) / new Vector2(input.Width>>1, input.Height>>1),
            Threshold = 1.0f
        };
        _clampShaderData.UpdateBuffer();


        int downSampleCount = GetDownSampleCount(input.Height);
        _downSampleTextures = new RenderTexture[downSampleCount];

        for (int i = 0; i < downSampleCount; i++)
        {
            uint width = input.Width >> (i + 1);
            uint height = input.Height >> (i + 1);


            if (i >= downSampleCount - 1)
            {
                float aspectRatio = (float)input.Width / input.Height;
                width = (uint)(_targetDownSampleHeight * aspectRatio);
                height = _targetDownSampleHeight;

                
                _downSampleTextures[i] = _renderingSystem.CreateRenderTexture(_backBufferPass, width, height);
            }
            else
            {
                _downSampleTextures[i] = _renderingSystem.CreateRenderTexture(_backBufferPass, width, height);
            }
        }

        int upSampleCount = downSampleCount - 1;
        _upSampleTextures = new RenderTexture[upSampleCount];

        for (int i = 0; i < upSampleCount; i++)
        {
            int offset = upSampleCount - i;
            uint width = input.Width >> (offset);
            uint height = input.Height >> (offset);

            if (i == 0)
            {
                _upSampleTextures[i] = _renderingSystem.CreateRenderTexture(_backBufferPass, width, height);
            }
            else
            {
                _upSampleTextures[i] = _renderingSystem.CreateRenderTexture(_backBufferPass, width, height);
            }
        }

    }

    public override void Blit(GPUFrameBuffer target)
    {
        if (_input == null)
        {
            throw new InvalidOperationException("Input is not set.");
        }

        Mesh mesh = FullScreenMesh;

        _command.Begin();

        RenderTexture clampFrame = _downSampleTextures![0];
        //clamp
        Vector2 invFrameSize;// = new Vector2(1f) / new Vector2(clampFrame!.Width, clampFrame.Height);
        //down sample
        using (var scope = _command.BeginRender(clampFrame.FrameBuffer))
        {
            scope.SetGraphicsPipeline(_clampPipelineInfo);
            uint indexCount = scope.SetMesh(mesh);
            scope.SetGraphicsResources(_clampShaderId_texture, _input!.ColorTextures[0].EntrySample);
            scope.SetGraphicsResources(_clampShaderId_data, _clampShaderData.EntryReadonly);
            scope.DrawIndexed(indexCount, 1, 0, 0, 0);
        }

        for (int i = 1; i < _downSampleTextures!.Length; i++)
        {
            RenderTexture downSampleFrame = _downSampleTextures[i];
            invFrameSize = new Vector2(1f) / new Vector2(downSampleFrame.Width, downSampleFrame.Height);
            using (var scope = _command.BeginRender(downSampleFrame.FrameBuffer))
            {
                scope.SetGraphicsPipeline(_downSamplePipelineInfo);
                uint indexCount = scope.SetMesh(mesh);
                scope.SetGraphicsResources(_downSampleShaderId_texture, _downSampleTextures![i - 1].ColorTextures[0].EntrySample);
                scope.PushGraphicsConstants(ShaderStage.Fragment, invFrameSize);
                scope.DrawIndexed(indexCount, 1, 0, 0, 0);
            }
        }


        //up sample

        using (var scope = _command.BeginRender(_upSampleTextures![0].FrameBuffer))
        {
            scope.SetGraphicsPipeline(_upSamplePipelineInfo);
            uint indexCount = scope.SetMesh(mesh);
            scope.SetGraphicsResources(_upSampleShaderId_previousTexture, _downSampleTextures![_downSampleTextures.Length - 1].ColorTextures[0].EntrySample);
            scope.SetGraphicsResources(_upSampleShaderId_currentTexture, _downSampleTextures![_downSampleTextures.Length - 2].ColorTextures[0].EntrySample);
            scope.DrawIndexed(indexCount, 1, 0, 0, 0);
        }
        


        for (int i = 1; i < _upSampleTextures!.Length; i++)
        {
            using (var scope = _command.BeginRender(_upSampleTextures[i].FrameBuffer))
            {
                scope.SetGraphicsPipeline(_upSamplePipelineInfo);
                uint indexCount = scope.SetMesh(mesh);
                scope.SetGraphicsResources(_upSampleShaderId_previousTexture, _upSampleTextures![i - 1].ColorTextures[0].EntrySample);
                scope.SetGraphicsResources(_upSampleShaderId_currentTexture, _downSampleTextures![_downSampleTextures.Length - i - 2].ColorTextures[0].EntrySample);
                scope.DrawIndexed(indexCount, 1, 0, 0, 0);
            }
        }


        if (_blitShader.TryUpdatePipelineContext(ref _blitPipelineInfo, target.AttachmentLayout))
        {
            _blitShaderId_texture = _blitPipelineInfo.GetResourceId(ShaderId_texture);
        }

        //blit
        using (var scope = _command.BeginRender(target)){
            scope.SetGraphicsPipeline(_blitPipelineInfo);
            uint indexCount = scope.SetMesh(mesh);
            scope.SetGraphicsResources(_blitShaderId_texture, _upSampleTextures![_upSampleTextures.Length - 1].ColorTextures[0].EntrySample);
            scope.DrawIndexed(indexCount, 1, 0, 0, 0);
        }

        _command.End();
        _renderingSystem.ScheduleCommandBuffer(_command);
    }

    private int GetDownSampleCount(uint height)
    {
        int count = 0;
        while (height > _targetDownSampleHeight)
        {
            height >>= 1;
            count++;
        }
        return count;
    }

    private void TryDisposeFrames()
    {
        if (_downSampleTextures != null)
        {
            for (int i = 0; i < _downSampleTextures.Length; i++)
            {
                _downSampleTextures[i].Dispose();
            }
        }

        if (_upSampleTextures != null)
        {
            for (int i = 0; i < _upSampleTextures.Length; i++)
            {
                _upSampleTextures[i].Dispose();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        //dispose non-private managed resources
        _command.Dispose();
        TryDisposeFrames();
    }
}