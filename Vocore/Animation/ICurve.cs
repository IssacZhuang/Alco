using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vocore
{
    public interface ICurveBae<T>
    {
        T Evaluate(float t);
        int PointsCount { get; }
        void SetPoints(IList<KeyFrame<T>> points);
        IList <KeyFrame<T>> Points { get; }
    }

    public interface ICurve: ICurveBae<float>
    {
    }

    public interface ICurve2D: ICurveBae<Vector2>
    {
    }

    public interface ICurve3D: ICurveBae<Vector3>
    {
    }

    public interface ICurve4D: ICurveBae<Vector4>
    {
    }
}

