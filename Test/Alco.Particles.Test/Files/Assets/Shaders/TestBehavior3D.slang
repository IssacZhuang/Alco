// file "TestBehavior3D.slang" - module "TestBehavior3D"
//
// Test fixture of the custom-behavior path: a 3D behavior that keeps the
// default spawn but adds a vortex swirl around the emitter's z axis. Composed
// with the emit/simulate templates by ValidateParticleShaders.

#language slang 2025
module TestBehavior3D;

import AlcoParticles_Core3D;

public struct TestBehavior3D : IParticleBehavior3D
{
    public override void simulate(inout ParticleData3D particle, EmitterParams3D emitter)
    {
        float dt = emitter.deltaTime;
        particle.lifetime -= dt;
        if (particle.lifetime <= 0.0)
        {
            return;
        }
        // Swirl around the z axis: tangential acceleration plus a slow lift.
        float3 tangent = normalize(float3(-particle.position.y, particle.position.x, 0.0) + float3(0.0, 0.0, 1e-3));
        particle.velocity += (tangent * 4.0 + float3(0.0, 0.0, 0.5)) * dt;
        particle.velocity *= exp(-0.1 * dt);
        particle.position += particle.velocity * dt;
        particle.rotation += particle.rollRate * dt;
        float age = 1.0 - particle.lifetime / max(particle.duration, 1e-5);
        particle.size = particle.startSize * lerp(1.0, emitter.size.z, age);
        particle.color = lerp(particle.startColor, emitter.colorEnd, age);
    }
}
