using Vocore.Engine;
using Vocore.Graphics;

public class Game : GameEngine
{
    public Game(GameEngineSetting setting) : base(setting)
    {
        BufferDescriptor descriptorOutput = new BufferDescriptor
        {
            Name = "GPUReadBack",
            Size = 1024,
            Usage = BufferUsage.CopyDst | BufferUsage.Storage
        };

        using GPUBuffer bufferOutput = GraphicsDevice.CreateBuffer(descriptorOutput);

        BufferDescriptor descriptorTmp = new BufferDescriptor
        {
            Name = "GPUReadBack",
            Size = 1024,
            Usage = BufferUsage.MapRead | BufferUsage.CopyDst
        };
        using GPUBuffer bufferTmp = GraphicsDevice.CreateBuffer(descriptorTmp);
    }

    override protected void OnUpdate(float delta)
    {
        
    }

    protected override void OnStop()
    {

    }
}