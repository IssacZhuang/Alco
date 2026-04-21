using Alco.Engine;
using Alco.Graphics;

public class Game : GameEngine
{
    public Game(GameEngineSetting setting) : base(setting)
    {
        uint length = 1024 * 1024 * 50;

        float[] data = new float[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = i;
        }
        BufferDescriptor descriptor = new BufferDescriptor
        {
            Name = "GPUReadBack",
            Size = length * sizeof(float),
            Usage = BufferUsage.CopyDst | BufferUsage.CopySrc | BufferUsage.Storage
        };

        using GPUBuffer buffer = GraphicsDevice.CreateBuffer(descriptor);
        GraphicsDevice.WriteBuffer(buffer, data);

        float[] output = new float[length];
        GraphicsDevice.ReadBuffer(buffer, output);

        for (int i = 0; i < 10; i++)
        {
            Console.WriteLine(output[i]);
        }

    }

    override protected void OnUpdate(float delta)
    {
        
    }

    protected override void OnStop()
    {

    }
}