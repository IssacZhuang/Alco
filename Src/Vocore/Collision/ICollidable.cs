using System;
using System.Collections.Generic;

namespace Vocore;

public interface ICollisionCaster
{
    void OnHit(object hitObject);
}   