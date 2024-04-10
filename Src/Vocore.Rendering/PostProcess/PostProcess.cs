using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class PostProcess : AutoDisposable
{
    private readonly GPUDevice _device;
    private readonly Shader _shader;
    private readonly Mesh _mesh;
    private readonly uint _shaderId_input;
    

    private GPUFrameBuffer? _input;
    private GPUResourceGroup? _inputGroup;

    protected Mesh FullScreenMesh => _mesh;
    protected Shader BlitShader => _shader;
    protected GPUResourceGroup? Input => _inputGroup;
    protected uint ShaderId_Input => _shaderId_input;

    internal PostProcess(RenderingSystem renderingSystem, Shader postProcessShader)
    {
        if (!postProcessShader.IsGraphicsShader)
        {
            throw new InvalidOperationException("ToneMap shader must be a graphics shader.");
        }

        _device = renderingSystem.GraphicsDevice;
        _shader = postProcessShader;
        _mesh = renderingSystem.FullScreenMesh;
        _shaderId_input = _shader.GetResourceId("texture");
    }

    public virtual void SetInput(GPUFrameBuffer input)
    {
        _inputGroup?.Dispose();
        _input = input;

        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _device.BindGroupTexture2DSampled,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, _input.ColorViews[0]),
                new ResourceBindingEntry(1, _device.SamplerLinearClamp)
            }
        );

        _inputGroup = _device.CreateResourceGroup(groupDescriptor);
    }

    public abstract void Blit(GPUFrameBuffer target);


    protected override void Dispose(bool disposing)
    {
        _inputGroup?.Dispose();
    }
}