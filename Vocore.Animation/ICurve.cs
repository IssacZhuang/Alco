using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vocore.Animation
{
    public interface ICurve
    {
        float Evaluate(float x);
        int PointsCount { get; }
        IEnumerable <Vector2> Points { get; }
    }
}

