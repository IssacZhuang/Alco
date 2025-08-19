using System;
using System.Collections.Generic;

namespace Alco;

public interface IRayCaster2D
{
    void OnHit(object hitObject, in RaycastHit2D hit, int userData);
}