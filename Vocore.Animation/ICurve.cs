using System;
using System.Collections.Generic;

namespace Vocore.Animation
{
    public interface ICurve
    {
        float Evaluate(float x);
    }
}

