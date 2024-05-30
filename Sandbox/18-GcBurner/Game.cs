using System.Numerics;
using Vocore.Engine;
using Vocore.Audio;
using Vocore;
using Vocore.Rendering;
using Vocore.GUI;

public class Game : GameEngine
{
    public const int Iteration = 1;
    public const float TickInterval = 0.5f;

    private float _timer = 0;

    public Game(GameEngineSetting setting) : base(setting)
    {

    }

    protected override void OnTick(float delta)
    {
        _timer += delta;
        if (_timer > TickInterval)
        {
            _timer -= TickInterval;
            for (int i = 0; i < Iteration; i++)
            {
                //test the garbage collector for both RAM and VRAM
                //load asset without cache
                Assets.Load<Font>("Font/Default.ttf", AssetCacheMode.None);
                Rendering.CreateGraphicsArrayBuffer<Vector3>(1000);
                Rendering.CreateRenderTexture(Rendering.DefaultRenderPass, 1280, 720);
            }
        }

    }

    override protected void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        DebugGUI.Text(FrameRate);
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
    }

    protected override void OnStop()
    {

    }
}