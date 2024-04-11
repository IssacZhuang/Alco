using System;

namespace Vocore.Engine
{
    public abstract class BaseEnginePlugin : IEnginePlugin
    {
        public virtual int Priority => 0;

        public virtual void OnInitilize(GameEngine engine)
        {

        }

        public virtual void OnStart()
        {

        }

        public virtual void OnExit()
        {

        }

    }
}