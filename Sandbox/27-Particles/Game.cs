using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;
using ImGuiNET;


public class Game : GameEngine
{


    public Game(GameEngineSetting setting) : base(setting)
    {
        
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        
    }

    
}