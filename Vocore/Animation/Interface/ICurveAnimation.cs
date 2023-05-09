using System;
using System.Collections.Generic;
using Unity.Mathematics;

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

    public interface ICurveAnimation2D : ICurveAnimationBase<float2>
    {
    }

    public interface ICurveAnimation3D : ICurveAnimationBase<float3>
    {
    }

    public interface ICurveAnimation4D : ICurveAnimationBase<float4>
    {
    }
}

