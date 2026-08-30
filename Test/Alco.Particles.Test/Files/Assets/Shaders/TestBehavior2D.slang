// file "TestBehavior2D.slang" - module "TestBehavior2D"
//
// Test fixture of the custom-behavior path: a 2D behavior that keeps the
// default spawn but simulates a gravity-well attractor instead of plain
// gravity. Composed with the emit/simulate templates by ValidateParticleShaders.

#language slang 2025
module TestBehavior2D;

import AlcoParticles_Core2D;

public struct TestBehavior2D : IParticleBehavior2D
{
    public override void simulate(inout ParticleData2D particle, EmitterParams2D emitter)
    {
        float dt = emitter.deltaTime;
        particle.lifetime -= dt;
        if (particle.lifetime <= 0.0)
        {
            return;
        }
        // Orbit the emitter origin: pull towards the center, keep the tangential speed.
        float2 toCenter = -particle.position;
        float distance = max(length(toCenter), 1e-3);
        particle.velocity += (toCenter / distance) * 200.0 * dt;
        particle.velocity *= exp(-0.2 * dt);
        particle.position += particle.velocity * dt;
        particle.rotation += particle.angularVelocity * dt;
        float age = 1.0 - particle.lifetime / max(particle.duration, 1e-5);
        particle.scale = particle.startScale * lerp(1.0, emitter.overLife.x, age);
        particle.color = lerp(particle.startColor, emitter.colorEnd, age);
    }
}
