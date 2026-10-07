// file "SbVortex2D.slang" - module "SbVortex2D"
//
// Sandbox 36-GpuParticles2D custom simulation behavior: a vortex. The default
// spawn (ring emission, radial speed) is kept; the simulation override swaps
// gravity for a tangential swirl with a slight inward pull, so particles spiral
// into the emitter origin. Linked into the GpuParticleEmit2D/Simulate2D pass
// templates through slang link-time specialization (IParticleBehavior2D).

#language slang 2025
module SbVortex2D;

import AlcoParticles_Core2D;

public struct SbVortex2D : IParticleBehavior2D
{
    public override void simulate(inout ParticleData2D particle, EmitterParams2D emitter)
    {
        float dt = emitter.deltaTime;
        particle.lifetime -= dt;
        if (particle.lifetime <= 0.0)
        {
            return;
        }
        // Swirl: tangential acceleration around the origin plus a weak inward pull.
        float2 toCenter = -particle.position;
        float distance = max(length(toCenter), 1e-3);
        float2 inward = toCenter / distance;
        float2 tangential = float2(-inward.y, inward.x);
        particle.velocity += (tangential * 60.0 + inward * 8.0) * dt;
        particle.velocity *= exp(-0.9 * dt);
        particle.position += particle.velocity * dt;
        particle.rotation += particle.angularVelocity * dt;
        float age = 1.0 - particle.lifetime / max(particle.duration, 1e-5);
        particle.scale = particle.startScale * lerp(1.0, emitter.overLife.x, age);
        particle.color = lerp(particle.startColor, emitter.colorEnd, age);
        float fadeIn = emitter.life.z > 1e-5 ? smoothstep(0.0, emitter.life.z, age) : 1.0;
        float fadeOut = emitter.life.w > 1e-5 ? 1.0 - smoothstep(1.0 - emitter.life.w, 1.0, age) : 1.0;
        particle.color.a *= fadeIn * fadeOut;
    }
}
