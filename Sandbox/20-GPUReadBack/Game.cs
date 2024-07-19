using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

public class Game : GameEngine
{
    public Game(GameEngineSetting setting) : base(setting)
    {
        uint length = 1024 * 1024 * 50;
        Profiler profiler = new Profiler();

        float[] data = new float[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = i;
        }
        BufferDescriptor descriptorOutput = new BufferDescriptor
        {
            Name = "GPUReadBack",
            Size = (uint)(length * sizeof(float)),
            Usage = BufferUsage.CopyDst | BufferUsage.CopySrc | BufferUsage.Storage
        };

        using GPUBuffer bufferOutput = GraphicsDevice.CreateBuffer(descriptorOutput);
        profiler.Start("write");
        GraphicsDevice.WriteBuffer(bufferOutput, data);
        ProfilerBlock write = profiler.End();
        Log.Info($"Time write: {write.Miliseconds}ms");

        float[] output = new float[length];
        profiler.Start("read");
        GraphicsDevice.ReadBuffer(bufferOutput, output);
        ProfilerBlock read = profiler.End();
        Log.Info($"Time read: {read.Miliseconds}ms");

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