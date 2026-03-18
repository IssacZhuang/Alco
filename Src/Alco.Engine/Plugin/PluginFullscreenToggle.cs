namespace Alco.Engine;

public class PluginFullscreenToggle : BaseEnginePlugin
{
    public override int Order => 0;

    public bool IsBorderlessFullscreen { get; set; } = true;

    public override void OnPostInitialize(GameEngine engine)
    {
        engine.AddSystem(new FullscreenToggleSystem(engine, IsBorderlessFullscreen));
    }

    private class FullscreenToggleSystem : BaseEngineSystem
    {
        private readonly GameEngine _engine;
        private readonly bool _isBorderlessFullscreen;

        public FullscreenToggleSystem(GameEngine engine, bool isBorderlessFullscreen)
        {
            _engine = engine;
            _isBorderlessFullscreen = isBorderlessFullscreen;
        }

        public override void OnUpdate(float delta)
        {
            bool altHeld = _engine.Input.IsKeyPressing(KeyCode.AltLeft)
                        || _engine.Input.IsKeyPressing(KeyCode.AltRight);
            bool enterDown = _engine.Input.IsKeyDown(KeyCode.Enter);

            if (altHeld && enterDown)
            {
                View view = _engine.MainView;
                if (view.WindowMode == WindowMode.BorderlessFullscreen
                    || view.WindowMode == WindowMode.ExclusiveFullscreen)
                {
                    uint2 screenSize = view.Size;
                    uint2 windowedSize = new uint2(
                        (uint)(screenSize.X * 0.75f),
                        (uint)(screenSize.Y * 0.75f));
                    view.WindowMode = WindowMode.Normal;
                    view.Size = windowedSize;
                    view.Position = new int2(
                        (int)(screenSize.X - windowedSize.X) / 2,
                        (int)(screenSize.Y - windowedSize.Y) / 2);
                }
                else
                {
                    view.WindowMode = _isBorderlessFullscreen
                        ? WindowMode.BorderlessFullscreen
                        : WindowMode.ExclusiveFullscreen;
                }
            }
        }
    }
}
