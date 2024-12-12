using System.Numerics;
using Vocore.Engine;
using Vocore.Audio;
using Vocore;
using Vocore.Rendering;
using Vocore.GUI;
using Vocore.Graphics;


/*Note: 

current problem:
collocter large amount of GPU will block the GPUQueue

*/

public class Game : GameEngine
{

    private class TestThreakWorkerItem : IThreadPoolWorkItem
    {
        public void Execute()
        {
            //do nothing
        }
    }

    private TestThreakWorkerItem _item = new TestThreakWorkerItem();

    private readonly ConcurrentPool<GPUCommandBuffer> _gpuCommandBufferPool;
    private readonly List<GPUCommandBuffer> _gpuCommandBufferList = new List<GPUCommandBuffer>();

    public Game(GameEngineSetting setting) : base(setting)
    {
        _gpuCommandBufferPool = new ConcurrentPool<GPUCommandBuffer>(CreateGPUCommandBuffer);
    }

    protected override void OnTick(float delta)
    {
        ThreadPool.UnsafeQueueUserWorkItem(_item, false);
        // Parallel.For(0, 100, ParallelCallback);
    }

    override protected void OnUpdate(float delta)
    {

        int count = 1000;
        for (int i = 0; i < count; i++)
        {
            _gpuCommandBufferList.Add(_gpuCommandBufferPool.Get());
        }

        for (int i = 0; i < count; i++)
        {
            _gpuCommandBufferPool.Return(_gpuCommandBufferList[i]);
        }

        _gpuCommandBufferList.Clear();

        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }



        DebugGUI.Text(FrameRate);

        if (DebugGUI.Button("Alloc 1"))
        {
            for (int i = 0; i < 1; i++)
            {
                AllocResource();
            }
        }

        if (DebugGUI.Button("Alloc 10"))
        {
            for (int i = 0; i < 10; i++)
            {
                AllocResource();
            }
        }

        if (DebugGUI.Button("Alloc 100"))
        {
            for (int i = 0; i < 100; i++)
            {
                AllocResource();
            }
        }

        if (DebugGUI.Button("Collect Gen 0"))
        {
            GC.Collect(0);
        }

        if (DebugGUI.Button("Collect Gen 1"))
        {
            GC.Collect(1);
        }

        if (DebugGUI.Button("Collect Gen 2"))
        {
            GC.Collect(2);
        }

        if (DebugGUI.Button("Collect All"))
        {
            GC.Collect();
        }

        TestSpanParam("1", "2", "3", "4", "5", "6", "7", "8", "9", "10");
    }

    protected override void OnStop()
    {
        
    }

    private void AllocResource()
    {
        //test the garbage collector for both RAM and VRAM
        //load asset without cache
        //Assets.Load<Font>("Font/Default.ttf", AssetCacheMode.None);
        Rendering.CreateGraphicsArrayBuffer<Vector3>(1000);
        Rendering.CreateRenderTexture(Rendering.PrefferedSDRPass, 1280, 720);
    }

    private void ParallelCallback(int index)
    {
        //to check if the Parallel.For uses Task or ThreadPool
    }

    private void TestSpanParam(params Span<string> spans)
    {
        DebugGUI.Text(spans.Length);
    }

    private GPUCommandBuffer CreateGPUCommandBuffer()
    {
        return GraphicsDevice.CreateCommandBuffer();
    }
}