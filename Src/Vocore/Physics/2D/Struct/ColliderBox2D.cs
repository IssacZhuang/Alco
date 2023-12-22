using System;
using System.Numerics;

namespace Vocore{
    public struct ColliderBox2D
    {
        public readonly ColliderType type => ColliderType.Box;
        public ShapeBox2D shape;


    }
}