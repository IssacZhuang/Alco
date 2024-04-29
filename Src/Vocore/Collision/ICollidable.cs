using System;
using System.Collections.Generic;

namespace Vocore;

public interface ICollisionCaster
{
    void OnHit(IReadOnlyList<object> hitObjects);
}   