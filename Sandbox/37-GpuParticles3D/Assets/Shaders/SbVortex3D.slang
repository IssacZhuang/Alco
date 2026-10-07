// file "SbVortex3D.slang" - module "SbVortex3D"
//
// Sandbox 37-GpuParticles3D custom simulation behavior: a vortex swirling
// around the emitter's Z axis. The default spawn (shell emission, radial speed)
// is kept; the simulation override swaps gravity for a tangential swirl with a
// slight inward pull. Linked into the GpuParticleEmit3D/Simulate3D pass
// templates through slang link-time specialization (IParticleBehavior3D).

#language slang 2025
module SbVortex3D;

import AlcoParticles_Core3D;

public struct SbVortex3D : IParticleBehavior3D
{
    public override void simulate(inout ParticleData3D particle, EmitterParams3D emitter)
    {
        float dt = emitter.deltaTime;
        particle.lifetime -= dt;
        if (particle.lifetime <= 0.0)
        {
            return;
        }
        // Swirl around the Z axis: tangential acceleration plus a weak inward pull.
        float3 toCenter = -particle.position;
        float distance = max(length(toCenter), 1e-3);
        float3 inward = toCenter / distance;
        float3 tangential = particleNormalizeSafe3D(cross(float3(0.0, 0.0, 1.0), inward), float3(0.0, 1.0, 0.0));
        particle.velocity += (tangential * 14.0 + inward * 2.5) * dt;
        particle.velocity *= exp(-1.0 * dt);
        particle.position += particle.velocity * dt;
        particle.rotation += particle.rollRate * dt;
        float age = 1.0 - particle.lifetime / max(particle.duration, 1e-5);
        particle.size = particle.startSize * lerp(1.0, emitter.size.z, age);
        particle.color = lerp(particle.startColor, emitter.colorEnd, age);
        float fadeIn = emitter.life.z > 1e-5 ? smoothstep(0.0, emitter.life.z, age) : 1.0;
        float fadeOut = emitter.life.w > 1e-5 ? 1.0 - smoothstep(1.0 - emitter.life.w, 1.0, age) : 1.0;
        particle.color.a *= fadeIn * fadeOut;
    }
}
