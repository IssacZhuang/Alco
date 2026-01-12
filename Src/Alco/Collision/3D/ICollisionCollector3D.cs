using System;

namespace Alco
{
    public interface IBvhCollisionCollector3D
    {
        bool OnHit(ColliderCastResult3D result);
    }

    public struct NativeListCollector3D : IBvhCollisionCollector3D
    {
        private unsafe NativeArrayList<ColliderCastResult3D>* _list;

        public unsafe NativeListCollector3D(NativeArrayList<ColliderCastResult3D>* list)
        {
            _list = list;
        }

        public unsafe bool OnHit(ColliderCastResult3D result)
        {
            _list->Add(result);
            return true;
        }
    }

    public struct FirstHitCollector3D : IBvhCollisionCollector3D
    {
        public ColliderCastResult3D Result;
        public bool HasHit;

        public bool OnHit(ColliderCastResult3D result)
        {
            Result = result;
            HasHit = true;
            return false;
        }
    }

    /// <summary>
    /// Interface for collecting collision results from CollisionWorld3D with the target object.
    /// </summary>
    public interface ICollisionCollector3D
    {
        /// <summary>
        /// Called when a target is hit.
        /// </summary>
        /// <param name="target">The target object that was hit.</param>
        /// <returns>True to continue the query, false to stop.</returns>
        bool OnHit(object target);
    }
}
