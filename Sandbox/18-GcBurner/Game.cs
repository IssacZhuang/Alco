using System.Numerics;
using Alco.Engine;
using Alco.Audio;
using Alco;
using Alco.Rendering;
using Alco.GUI;
using Alco.Graphics;
using Alco.ImGUI;


/*Note: 

current problem:
collocter large amount of GPU will block the GPUQueue

*/

public class Game : GameEngine
{

    private class TestThreadWorkerItem : IThreadPoolWorkItem
    {
        public void Execute()
        {
            //do nothing
        }
    }

    private class TestParallelTask : ReuseableBatchTask
    {
        protected override void ExecuteCore(int index)
        {
            //empty
        }
    }

    private TestThreadWorkerItem _item = new TestThreadWorkerItem();

    private readonly ConcurrentPool<GPUCommandBuffer> _gpuCommandBufferPool;
    private readonly List<GPUCommandBuffer> _gpuCommandBufferList = new List<GPUCommandBuffer>();
    private readonly TestParallelTask _task = new TestParallelTask();

    public Game(GameEngineSetting setting) : base(setting)
    {
        _gpuCommandBufferPool = new ConcurrentPool<GPUCommandBuffer>(CreateGPUCommandBuffer);
    }

    protected override void OnTick(float delta)
    {
        ThreadPool.UnsafeQueueUserWorkItem(_item, false);
        _task.RunParallel(10000);
        // Parallel.For(0, 100, ParallelCallback);
    }

    override protected void OnUpdate(float delta)
    {

        DebugStats.Text(FrameRate);

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

        // ImGUI Controls
        ImGui.Begin("GC Burner Controls");

        if (ImGui.Button("Alloc 1"))
        {
            for (int i = 0; i < 1; i++)
            {
                AllocResource();
            }
        }

        if (ImGui.Button("Alloc 10"))
        {
            for (int i = 0; i < 10; i++)
            {
                AllocResource();
            }
        }

        if (ImGui.Button("Alloc 100"))
        {
            for (int i = 0; i < 100; i++)
            {
                AllocResource();
            }
        }

        if (ImGui.Button("Collect Gen 0"))
        {
            GC.Collect(0);
        }

        if (ImGui.Button("Collect Gen 1"))
        {
            GC.Collect(1);
        }

        if (ImGui.Button("Collect Gen 2"))
        {
            GC.Collect(2);
        }

        if (ImGui.Button("Collect All"))
        {
            GC.Collect();
        }

        // Display test span param info
        ImGui.Separator();
        ImGui.Text("Test Span Length: 10");

        ImGui.End();

        TestSpanParam("1", "2", "3", "4", "5", "6", "7", "8", "9", "10");
    }

    protected override void OnStop()
    {
        
    }

    private void AllocResource()
    {
        //test the garbage collector for both RAM and VRAM
        //load asset without cache
        //AssetSystem.Load<Font>("Font/Default.ttf", AssetCacheMode.None);
        RenderingSystem.CreateGraphicsArrayBuffer<Vector3>(1000);
        RenderingSystem.CreateRenderTexture(RenderingSystem.PrefferedSDRPass, 1280, 720);
    }

    private void ParallelCallback(int index)
    {
        //to check if the Parallel.For uses Task or ThreadPool
    }

    private void TestSpanParam(params Span<string> spans)
    {
        // Display span length in the ImGUI window (handled in OnUpdate)
    }

    private GPUCommandBuffer CreateGPUCommandBuffer()
    {
        return GraphicsDevice.CreateCommandBuffer();
    }
}