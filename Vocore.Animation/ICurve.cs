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
}

