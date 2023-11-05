using System;

namespace Vocore
{
    interface IEnginePlugin
    {
        void OnEngineCreate(GameEngine engine);
        void OnEngineExit();
    }
}