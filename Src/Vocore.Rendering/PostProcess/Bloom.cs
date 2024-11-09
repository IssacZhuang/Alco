using System.Numerics;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class Bloom : PostProcess
{
    private struct ClampShaderData
    {
        public Vector2 InvFrameSize;
        public float Threshold;
    }

    public const string ShaderId_texture = "texture";
    public const string ShaderId_data = "data";
    public const string ShaderId_previousTexture = "previousTexture";
    public const string ShaderId_currentTexture = "currentTexture";

    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandDownSample;
    private readonly GPUCommandBuffer _commandBlit;
    private readonly GPURenderPass _backBufferPass;
    private readonly RenderingSystem _renderingSystem;

    private readonly Shader _blitShader;
    private  ShaderPipelineInfo _blitPipelineInfo;
    private uint _blitShaderId_texture;

    protected GPUFrameBuffer? _input;
    protected GPUResourceGroup? _inputGroup;

    private readonly GraphicsValueBuffer<ClampShaderData> _clampShaderData;

    //for clamp
    private readonly Shader _clampShader;
    private ShaderPipelineInfo _clampPipelineInfo;
    private readonly uint _clampShaderId_texture;
    private readonly uint _clampShaderId_data;

    private readonly Shader _downSampleShader;
    private ShaderPipelineInfo _downSamplePipelineInfo;
    private uint _downSampleShaderId_texture;


    private readonly uint _targetDownSampleHeight;
    private GPUFrameBuffer[]? _downSampleFrames;
    private GPUResourceGroup[]? _downSampleGroups;

    private readonly Shader _upSampleShader;
    private ShaderPipelineInfo _upSamplePipelineInfo;
    private readonly uint _upSampleShaderId_previousTexture;
    private readonly uint _upSampleShaderId_currentTexture;

    private GPUFrameBuffer[]? _upSampleFrames;
    private GPUResourceGroup[]? _upSampleGroups;
    internal Bloom(RenderingSystem renderingSystem, Shader blitShader, Shader clampShader, Shader downSampleShader, Shader upSampleShader, uint targetDownSampleHeight) : base(renderingSystem, blitShader)
    {
        _device = renderingSystem.GraphicsDevice;
        _renderingSystem = renderingSystem;
        _targetDownSampleHeight = targetDownSampleHeight;

        RenderPassDescriptor descriptor = new RenderPassDescriptor(
            [new(_device.PrefferedHDRFormat)],
            null
        );

        _backBufferPass = _device.CreateRenderPass(descriptor);

        _blitShader = blitShader;
        _blitPipelineInfo = blitShader.GetGraphicsPipeline(renderingSystem.PrefferedSDRPass);
        _blitShaderId_texture = _blitPipelineInfo.GetResourceId(ShaderId_texture);

        _clampShader = clampShader;
        _clampPipelineInfo = clampShader.GetGraphicsPipeline(_backBufferPass);
        _clampShaderData = renderingSystem.CreateGraphicsValueBuffer<ClampShaderData>("bloom_threshold");
        _clampShaderId_texture = _clampPipelineInfo.GetResourceId(ShaderId_texture);
        _clampShaderId_data = _clampPipelineInfo.GetResourceId(ShaderId_data);

        _downSampleShader = downSampleShader;
        _downSamplePipelineInfo = downSampleShader.GetGraphicsPipeline(_backBufferPass);
        _downSampleShaderId_texture = _downSamplePipelineInfo.GetResourceId(ShaderId_texture);

        _upSampleShader = upSampleShader;
        _upSamplePipelineInfo = upSampleShader.GetGraphicsPipeline(_backBufferPass);
        _upSampleShaderId_previousTexture = _upSamplePipelineInfo.GetResourceId(ShaderId_previousTexture);
        _upSampleShaderId_currentTexture = _upSamplePipelineInfo.GetResourceId(ShaderId_currentTexture);

        _commandDownSample = _device.CreateCommandBuffer();
        _commandBlit = _device.CreateCommandBuffer();
    }

    public override void SetInput(GPUFrameBuffer input)
    {
        base.SetInput(input);

        _input = input;
        _inputGroup?.Dispose();

        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _device.BindGroupTexture2DSampled,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, _input.ColorViews[0]),
                new ResourceBindingEntry(1, _device.SamplerLinearClamp)
            }
        );

        _inputGroup = _device.CreateResourceGroup(groupDescriptor);

        TryDisposeFrames();

        _clampShaderData.Value = new ClampShaderData
        {
            InvFrameSize = new Vector2(1f) / new Vector2(input.Width>>1, input.Height>>1),
            Threshold = 1.0f
        };
        _clampShaderData.UpdateBuffer();


        int downSampleCount = GetDownSampleCount(input.Height);
        _downSampleFrames = new GPUFrameBuffer[downSampleCount];
        _downSampleGroups = new GPUResourceGroup[downSampleCount];

        for (int i = 0; i < downSampleCount; i++)
        {
            uint width = input.Width >> (i + 1);
            uint height = input.Height >> (i + 1);


            if (i >= downSampleCount - 1)
            {
                float aspectRatio = (float)input.Width / input.Height;
                width = (uint)(_targetDownSampleHeight * aspectRatio);
                height = _targetDownSampleHeight;

                
                CreateFrameBuffer(width, height, $"bloom_down_sample_frame{i}", out _downSampleFrames[i], out _downSampleGroups[i]);
            }
            else
            {
                CreateFrameBuffer(width, height, $"bloom_down_sample_frame{i}", out _downSampleFrames[i], out _downSampleGroups[i]);
            }
        }

        int upSampleCount = downSampleCount - 1;
        _upSampleFrames = new GPUFrameBuffer[upSampleCount];
        _upSampleGroups = new GPUResourceGroup[upSampleCount];

        for (int i = 0; i < upSampleCount; i++)
        {
            int offset = upSampleCount - i;
            uint width = input.Width >> (offset);
            uint height = input.Height >> (offset);

            if (i == 0)
            {
                CreateFrameBuffer(width, height, $"bloom_up_sample_frame{i}", out _upSampleFrames[i], out _upSampleGroups[i]);
            }
            else
            {
                CreateFrameBuffer(width, height, $"bloom_up_sample_frame{i}", out _upSampleFrames[i], out _upSampleGroups[i]);
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

        _commandDownSample.Begin();

        GPUFrameBuffer clampFrame = _downSampleFrames![0];
        //clamp
        Vector2 invFrameSize;// = new Vector2(1f) / new Vector2(clampFrame!.Width, clampFrame.Height);

        _commandDownSample.SetFrameBuffer(clampFrame);
        _commandDownSample.SetGraphicsPipeline(_clampPipelineInfo);
        _commandDownSample.SetVertexBuffer(0, mesh.VertexBuffer);
        _commandDownSample.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        _commandDownSample.SetGraphicsResources(_clampShaderId_texture, _inputGroup!);
        _commandDownSample.SetGraphicsResources(_clampShaderId_data, _clampShaderData.EntryReadonly);
        _commandDownSample.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);


        //down sample
        for (int i = 1; i < _downSampleFrames!.Length; i++)
        {
            invFrameSize = new Vector2(1f) / new Vector2(_downSampleFrames[i].Width, _downSampleFrames[i].Height);
            _commandDownSample.SetFrameBuffer(_downSampleFrames[i]);
            _commandDownSample.SetGraphicsPipeline(_downSamplePipelineInfo);
            _commandDownSample.SetVertexBuffer(0, mesh.VertexBuffer);
            _commandDownSample.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
            _commandDownSample.SetGraphicsResources(_downSampleShaderId_texture, _downSampleGroups![i - 1]);
            _commandDownSample.PushConstants(ShaderStage.Fragment, invFrameSize);
            _commandDownSample.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
        }
        _commandDownSample.End();
        _device.Submit(_commandDownSample);

        //up sample
        //invFrameSize = new Vector2(1f) / new Vector2(_upSampleFrames![0].Width, _upSampleFrames![0].Height);
        _commandDownSample.Begin();
        _commandDownSample.SetFrameBuffer(_upSampleFrames![0]);
        _commandDownSample.SetGraphicsPipeline(_upSamplePipelineInfo);
        _commandDownSample.SetVertexBuffer(0, mesh.VertexBuffer);
        _commandDownSample.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        _commandDownSample.SetGraphicsResources(_upSampleShaderId_previousTexture, _downSampleGroups![_downSampleGroups.Length - 1]);
        _commandDownSample.SetGraphicsResources(_upSampleShaderId_currentTexture, _downSampleGroups![_downSampleGroups.Length - 2]);
        //_commandDownSample.PushConstants(ShaderStage.Fragment, invFrameSize);
        _commandDownSample.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
        


        for (int i = 1; i < _upSampleFrames!.Length; i++)
        {
            //invFrameSize = new Vector2(1f) / new Vector2(_upSampleFrames[i].Width, _upSampleFrames[i].Height);

            _commandDownSample.SetFrameBuffer(_upSampleFrames[i]);
            _commandDownSample.SetGraphicsPipeline(_upSamplePipelineInfo);
            _commandDownSample.SetVertexBuffer(0, mesh.VertexBuffer);
            _commandDownSample.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
            _commandDownSample.SetGraphicsResources(_upSampleShaderId_previousTexture, _upSampleGroups![i - 1]);
            _commandDownSample.SetGraphicsResources(_upSampleShaderId_currentTexture, _downSampleGroups![_downSampleGroups.Length - i - 2]);
            //_commandDownSample.PushConstants(ShaderStage.Fragment, invFrameSize);
            _commandDownSample.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
        }

        _commandDownSample.End();
        _device.Submit(_commandDownSample);

        // if (_blitPass != target.RenderPass)
        // {
        //     _blitPass = target.RenderPass;
        //     _blitPipeline = _blitShader.GetPipelineVariant(_blitPass);
        // }
        if(_blitShader.TryUpdatePipelineInfo(ref _blitPipelineInfo, target.RenderPass))
        {
            _blitShaderId_texture = _blitPipelineInfo.GetResourceId(ShaderId_texture);
        }

        //blit
        _commandBlit.Begin();
        _commandBlit.SetFrameBuffer(target);
        _commandBlit.SetGraphicsPipeline(_blitPipelineInfo);
        _commandBlit.SetVertexBuffer(0, mesh.VertexBuffer);
        _commandBlit.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        _commandBlit.SetGraphicsResources(_blitShaderId_texture, _upSampleGroups![_upSampleGroups.Length - 1]);
        _commandBlit.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
        _commandBlit.End();
        _device.Submit(_commandBlit);
    }

    private void CreateFrameBuffer(uint width, uint height, string name, out GPUFrameBuffer frameBuffer, out GPUResourceGroup group)
    {
        FrameBufferDescriptor desc = new FrameBufferDescriptor(
            _backBufferPass,
            width,
            height,
            name
        );

        frameBuffer = _device.CreateFrameBuffer(desc);

        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _device.BindGroupTexture2DSampled,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, frameBuffer.ColorViews[0]),
                new ResourceBindingEntry(1, _device.SamplerLinearClamp)
            }
        );

        group = _device.CreateResourceGroup(groupDescriptor);
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
        if (_downSampleFrames != null)
        {
            for (int i = 0; i < _downSampleFrames.Length; i++)
            {
                _downSampleFrames[i].Dispose();
            }
        }

        if (_downSampleGroups != null)
        {
            for (int i = 0; i < _downSampleGroups.Length; i++)
            {
                _downSampleGroups[i].Dispose();
            }
        }

        if (_upSampleFrames != null)
        {
            for (int i = 0; i < _upSampleFrames.Length; i++)
            {
                _upSampleFrames[i].Dispose();
            }
        }

        if (_upSampleGroups != null)
        {
            for (int i = 0; i < _upSampleGroups.Length; i++)
            {
                _upSampleGroups[i].Dispose();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        //dispose non-private managed resources
        _inputGroup?.Dispose();
        _commandDownSample.Dispose();
        TryDisposeFrames();
    }
}