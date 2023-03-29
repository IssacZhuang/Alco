using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public interface ICurveAnimation<T> where T:unmanaged
    {
        IReadOnlyList<CurvePoint<float>> TimeCurve{get;}
        IReadOnlyList<CurvePoint<T>> ValueCurve{get;}
    }
}

