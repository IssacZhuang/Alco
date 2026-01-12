using System;

namespace Alco
{
    public interface ICollisionCollector<TResult>
    {
        bool AddHit(TResult result);
    }

    public struct NativeListCollector : ICollisionCollector<ColliderCastResult2D>
    {
        private unsafe NativeArrayList<ColliderCastResult2D>* _list;

        public unsafe NativeListCollector(NativeArrayList<ColliderCastResult2D>* list)
        {
            _list = list;
        }

        public unsafe bool AddHit(ColliderCastResult2D result)
        {
            _list->Add(result);
            return true;
        }
    }

    public struct FirstHitCollector : ICollisionCollector<ColliderCastResult2D>
    {
        public ColliderCastResult2D Result;
        public bool HasHit;

        public bool AddHit(ColliderCastResult2D result)
        {
            Result = result;
            HasHit = true;
            return false;
        }
    }
}
