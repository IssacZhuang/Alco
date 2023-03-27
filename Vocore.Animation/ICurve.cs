using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vocore.Animation
{
    public interface ICurve
    {
        float Evaluate(float t);
        int PointsCount { get; }
        IList <KeyFrame> Points { get; }
    }

    public interface ICurve2D{
        Vector2 Evaluate(float t);
        int PointsCount { get; }
        IList <KeyFrame2D> Points { get; }
    }

    public interface ICurve3D{
        Vector3 Evaluate(float t);
        int PointsCount { get; }
        IList <KeyFrame3D> Points { get; }
    }

    public interface ICurve4D{
        Vector4 Evaluate(float t);
        int PointsCount { get; }
        IList <KeyFrame4D> Points { get; }
    }
}

