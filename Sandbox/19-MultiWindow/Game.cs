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
    
        DebugGUI.Text(MainWindow.Position.ToString());


    }

    protected override void OnStop()
    {
        
    }
}