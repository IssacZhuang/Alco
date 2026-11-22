// file "TrailInheritedSurface.slang" - module "TrailInheritedSurface"
// Exercises both default methods of the shared trail surface contract.

#language slang 2025
module TrailInheritedSurface;

import Core;
import AlcoRendering_TrailSurface;

public struct Surface : ITrailSurface
{
    public typedef EmptyTrailParams Params;
}
