using Alco;
using Alco.Engine;
using Alco.Graphics;

GraphicsBackend backend = GraphicsBackend.Auto;
for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    if (arg.StartsWith("--backend=", StringComparison.OrdinalIgnoreCase))
    {
        if (!Enum.TryParse<GraphicsBackend>(arg["--backend=".Length..], ignoreCase: true, out backend) ||
            !Enum.IsDefined(backend))
        {
            Log.Error($"Invalid --backend value: {arg}");
            return 1;
        }
    }
}

GameEngineSetting setting = new()
{
    StopWhenError = true,
    View = new ViewSetting(1280, 720, "FXAA Anti-Aliasing"),
    Graphics = GraphicsSetting.Default with
    {
        Backend = backend,
    },
};

using (Game game = new(setting, args))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();
return 0;
