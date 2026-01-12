using System;

namespace Alco
{
    public interface IBvhCollisionCollector
    {
        bool OnHit(ColliderCastResult2D result);
    }

    public struct NativeListCollector : IBvhCollisionCollector
    {
        private unsafe NativeArrayList<ColliderCastResult2D>* _list;

        public unsafe NativeListCollector(NativeArrayList<ColliderCastResult2D>* list)
        {
            _list = list;
        }

        public unsafe bool OnHit(ColliderCastResult2D result)
        {
            _list->Add(result);
            return true;
        }
    }

    public struct FirstHitCollector : IBvhCollisionCollector
    {
        public ColliderCastResult2D Result;
        public bool HasHit;

        public bool OnHit(ColliderCastResult2D result)
        {
            Result = result;
            HasHit = true;
            return false;
        }
    }

    /// <summary>
    /// Interface for collecting collision results from CollisionWorld2D with the target object.
    /// </summary>
    public interface ICollisionCollector
    {
        /// <summary>
        /// Called when a target is hit.
        /// </summary>
        /// <param name="target">The target object that was hit.</param>
        /// <returns>True to continue the query, false to stop.</returns>
        bool OnHit(object target);
    }
}
