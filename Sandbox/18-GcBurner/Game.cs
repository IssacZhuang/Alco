using System.Numerics;
using Vocore.Engine;
using Vocore.Audio;
using Vocore;
using Vocore.Rendering;
using Vocore.GUI;


/*Note: 

current problem:
collocter large amount of GPU will block the GPUQueue

*/

public class Game : GameEngine
{

    public Game(GameEngineSetting setting) : base(setting)
    {

    }



    override protected void OnUpdate(float delta)
    {
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
        Rendering.CreateRenderTexture(Rendering.DefaultRenderPass, 1280, 720);
    }
}