using System;
using Vocore.Engine;
using Veldrid;

public class Game : GameEngine
{
    protected override void OnStart()
    {

    }

    public string LoadAsset(string path)
    {
        return File.ReadAllText(Path.Combine(WorkingDirectory, path));
    }
}