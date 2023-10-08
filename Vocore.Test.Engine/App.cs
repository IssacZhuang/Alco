using System;
using Vocore;
using Vocore.Rendering;

public class App:Engine{
    public App(string name):base(name){
        
    }

    protected override void OnWindowResize(int width, int height)
    {
        Log.Info($"Window resized to {width}x{height}");
    }
}