// file "AlcoParticles_Default3D.slang" - module "AlcoParticles_Default3D"
//
// The built-in 3D particle behavior: every method of the contract takes its
// default implementation (see AlcoParticles_Core3D). 3D groups without an
// explicit behavior module compose with this one.

#language slang 2025
module AlcoParticles_Default3D;

import AlcoParticles_Core3D;

public struct DefaultBehavior3D : IParticleBehavior3D {}
