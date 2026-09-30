// file "AlcoParticles_Default2D.slang" - module "AlcoParticles_Default2D"
//
// The built-in 2D particle behavior: every method of the contract takes its
// default implementation (shape emission, gravity, drag, color/size over life,
// fade in/out — see AlcoParticles_Core2D). 2D groups without an explicit
// behavior module compose with this one.

#language slang 2025
module AlcoParticles_Default2D;

import AlcoParticles_Core2D;

public struct DefaultBehavior2D : IParticleBehavior2D {}
