using System;
using System.Collections.Generic;

namespace Alco;

public interface ICollisionCaster
{
    void OnHit(object hitObject, int userData);
}   