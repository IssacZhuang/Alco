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