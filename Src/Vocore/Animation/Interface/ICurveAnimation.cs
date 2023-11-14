using System;
using System.Numerics;
using System.Collections.Generic;


namespace Vocore
{
    public interface ICurveAnimationBase<T> where T:unmanaged
    {
        float Duration { get; }
        T Evaluate(float t);
        void BindEvent(string name, Action action);
        void UnbindEvent(string name, Action action);
    }

    public interface ICurveAnimation : ICurveAnimationBase<float>
    {
    }

    public interface ICurveAnimation2D : ICurveAnimationBase<Vector2>
    {
    }

    public interface ICurveAnimation3D : ICurveAnimationBase<Vector3>
    {
    }

    public interface ICurveAnimation4D : ICurveAnimationBase<Vector4>
    {
    }
}

