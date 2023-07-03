using System;

namespace Vocore
{
    public interface IEntity
    {
        void OnCreate();
        void OnSpawn();
        void OnDespawn();
        void OnDestroy();
        void OnTick();
        void OnUpdate();
    }
}