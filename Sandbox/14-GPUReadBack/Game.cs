using Alco.Engine;
using Alco.Graphics;

/// <summary>Sandbox verifying a GPU buffer write and readback round trip.</summary>
public class Game : GameEngine
{
    /// <summary>Runs the buffer write/readback round trip.</summary>
    /// <param name="setting">Engine settings for the game instance.</param>
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
}