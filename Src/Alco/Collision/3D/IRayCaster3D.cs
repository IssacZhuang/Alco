using System;
using System.Collections.Generic;

namespace Alco;

public interface IRayCaster3D
{
    void OnHit(object hitObject, in RaycastHit3D hit, int userData);
}