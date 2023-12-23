using System;

namespace Vocore.Engine
{
    public class PluginForceMouseInScreenCenter : BaseEnginePlugin
    {
        public override int Priority => -1000;

        public override void OnInitilize(GameEngine engine, ref GameEngineSetting setting)
        {
            engine.Input.ForceMouseInScreenCenter = true;
            engine.Input.ResetMouseToCenter();
        }
    }
}