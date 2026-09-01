// file "AlcoParticles_Core3D.slang" - module "AlcoParticles_Core3D"
//
// The 3D GPU particle contract: the particle record, the per-emitter parameter
// block (CPU-authored every frame), the hash RNG and the simulation behavior
// interface with full default implementations. 3D particle records and emitter
// parameters are deliberately separate from their 2D counterparts — the two
// data structures never mix. Custom behavior modules export exactly one struct
// implementing IParticleBehavior3D; the emit/simulate pass templates compose
// with the module through slang link-time specialization.
//
// The C# twins of ParticleData3D / EmitterParams3D (Alco.Particles) must match
// these layouts field for field. GpuDrawArgs is shared (AlcoParticles_Core2D).

#language slang 2025
module AlcoParticles_Core3D;

import AlcoRendering_Core;
import AlcoParticles_Core2D;

// ── particle record (84 bytes) ──────────────────────────────────────────────

public struct ParticleData3D
{
    public float3 position;        // world (or emitter-local) position
    public float size;             // current quad extent (uniform)
    public float4 color;           // current color (color-over-life and fades applied)
    public float4 startColor;      // spawn color, the lerp source of color-over-life
    public float3 velocity;
    public float startSize;        // spawn size, the lerp source of size-over-life
    public float rotation;         // billboard roll, radians
    public float rollRate;         // billboard roll velocity, radians per second
    public float lifetime;         // remaining seconds; <= 0 marks a dead (free) slot
    public float duration;         // total seconds
    public uint animStart;         // flipbook: first frame of the anim picked at spawn
};

// ── per-emitter parameters (320 bytes, one per emitter slot, CPU-written) ───

public struct EmitterParams3D
{
    // per-frame control
    public uint spawnCount;        // particles to emit this frame
    public uint emitCursor;        // ring-buffer write cursor of this frame
    public uint capacity;          // slice capacity of this emitter
    public uint sliceOffset;       // absolute base slot of this emitter in the pool
    // frame data
    public float deltaTime;
    public float emitterTime;      // seconds on the emitter timeline
    public uint frameSeed;         // per-frame RNG seed
    public uint flags;             // bit0: world-space simulation, bit1: color gradient lookup,
                                   // bit2: size curve lookup, bit3: velocity-stretched billboards,
                                   // bit4: flipbook plays in reverse
                                   // (PARTICLE_FLAG_* constants, AlcoParticles_Core2D)
    // emitter transform (spawn only, world space)
    public float4x4 worldMatrix;
    // emission shape: x = type (0 point, 1 sphere, 2 hemisphere, 3 box),
    // y = radius, z = hollow inner radius fraction [0, 1]
    public float4 shape;
    public float4 extents;         // xyz = box half extents
    // emission direction: xyz = base direction, w = cone spread half-angle (rad)
    public float4 emission;
    public float4 speed;           // x = min, y = max, z = direction mode (0 constant, 1 radial), w = stretch speed scale
    // life: x = lifetime min, y = lifetime max, z = fade-in fraction, w = fade-out fraction
    public float4 life;
    public float4 size;            // x = min, y = max, z = end scale multiplier, w = stretch length scale
    public float4 rotation;        // x = roll min, y = roll max, z = roll rate min, w = roll rate max
    public float4 motion;          // xyz = gravity, w = drag
    public float4 colorMin;
    public float4 colorMax;
    public float4 colorEnd;
    // render
    public float4 tint;
    public float4 flipbook;        // x = rows, y = cols, z = lifetime-relative cycles,
                                   // w = frames per anim (0 = whole sheet, one anim)
    public uint4 renderMisc;       // x = quad mesh index count, y = custom per-instance
                                   // data (float bits; surfaced to material vertex hooks
                                   // as ParticleVertexInput.customData)
};

// ── default spawn/simulate pieces (reusable by custom behaviors) ────────────

// Samples the emission shape in emitter-local space.
public float3 particleSampleShape3D(EmitterParams3D emitter, inout uint rng)
{
    uint type = uint(emitter.shape.x);
    if (type == 1u || type == 2u) // sphere / hemisphere
    {
        // Uniform direction on the unit sphere (Marsaglia).
        float u = particleRandom(rng) * 2.0 - 1.0;
        float angle = particleRandom(rng) * TAU;
        float ring = sqrt(max(1.0 - u * u, 0.0));
        float3 direction = float3(ring * cos(angle), ring * sin(angle), u);
        if (type == 2u) // hemisphere: flip into the +z half space
        {
            direction.z = abs(direction.z);
        }
        float inner = clamp(emitter.shape.z, 0.0, 1.0);
        float radius = emitter.shape.y * pow(lerp(inner * inner * inner, 1.0, particleRandom(rng)), 1.0 / 3.0);
        return direction * radius;
    }
    if (type == 3u) // box
    {
        return (float3(particleRandom(rng), particleRandom(rng), particleRandom(rng)) * 2.0 - 1.0)
            * emitter.extents.xyz;
    }
    return float3(0.0, 0.0, 0.0);
}

// Picks the initial velocity from the emission direction parameters.
public float3 particleSampleVelocity3D(EmitterParams3D emitter, float3 localPosition, inout uint rng)
{
    float3 direction;
    if (emitter.speed.z > 0.5) // radial
    {
        float lengthSquared = dot(localPosition, localPosition);
        direction = lengthSquared > 1e-8 ? localPosition * rsqrt(lengthSquared)
            : float3(1.0, 0.0, 0.0);
    }
    else
    {
        // Randomize the base direction inside the spread cone: rotate a random
        // unit vector by a uniform angle in [0, spread] around a random axis
        // orthogonal to the base direction.
        float3 baseDirection = particleNormalizeSafe3D(emitter.emission.xyz, float3(1.0, 0.0, 0.0));
        float spread = emitter.emission.w;
        if (spread > 1e-4)
        {
            float3 axis = normalize(cross(baseDirection, abs(baseDirection.z) < 0.99
                ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0)));
            axis = particleRotateAxis(axis, baseDirection, particleRandom(rng) * TAU);
            direction = particleRotateAxis(baseDirection, axis, particleRandom(rng) * spread);
        }
        else
        {
            direction = baseDirection;
        }
    }
    return direction * particleRange(rng, emitter.speed.x, emitter.speed.y);
}

public float3 particleNormalizeSafe3D(float3 v, float3 fallback)
{
    float lengthSquared = dot(v, v);
    return lengthSquared > 1e-8 ? v * rsqrt(lengthSquared) : fallback;
}

// Rodrigues rotation of v by `radians` around the unit axis `axis`.
public float3 particleRotateAxis(float3 v, float3 axis, float radians)
{
    float s = sin(radians);
    float c = cos(radians);
    return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
}

// The default spawn: shape sample, velocity pick, lifetime/size/roll/color
// ranges, optional transform into world space.
public void particleSpawnDefault3D(inout ParticleData3D particle, EmitterParams3D emitter, inout uint rng)
{
    float3 localPosition = particleSampleShape3D(emitter, rng);
    float3 velocity = particleSampleVelocity3D(emitter, localPosition, rng);
    float3 position = localPosition;
    if ((emitter.flags & PARTICLE_FLAG_WORLD_SPACE) != 0u) // world space: spawn through the emitter transform
    {
        position = mul(emitter.worldMatrix, float4(localPosition, 1.0)).xyz;
        velocity = mul(emitter.worldMatrix, float4(velocity, 0.0)).xyz;
    }
    particle.position = position;
    particle.velocity = velocity;
    particle.duration = particleRange(rng, emitter.life.x, emitter.life.y);
    particle.lifetime = particle.duration;
    particle.startSize = particleRange(rng, emitter.size.x, emitter.size.y);
    particle.size = particle.startSize;
    particle.rotation = particleRange(rng, emitter.rotation.x, emitter.rotation.y);
    particle.rollRate = particleRange(rng, emitter.rotation.z, emitter.rotation.w);
    particle.startColor = float4(
        particleRandom(rng), particleRandom(rng), particleRandom(rng), particleRandom(rng))
        * (emitter.colorMax - emitter.colorMin) + emitter.colorMin;
    particle.color = particle.startColor;
    particle.animStart = particlePickFlipbookAnim(emitter.flipbook, rng);
}

// The default simulate: gravity, exponential drag, integration, roll,
// color/size over life, fade in/out.
public void particleSimulateDefault3D(inout ParticleData3D particle, EmitterParams3D emitter)
{
    float dt = emitter.deltaTime;
    particle.lifetime -= dt;
    if (particle.lifetime <= 0.0)
    {
        return;
    }
    float age = 1.0 - particle.lifetime / max(particle.duration, 1e-5);
    particle.velocity += emitter.motion.xyz * dt;
    particle.velocity *= exp(-emitter.motion.w * dt);
    particle.position += particle.velocity * dt;
    particle.rotation += particle.rollRate * dt;
    particle.size = particle.startSize * lerp(1.0, emitter.size.z, age);
    float4 color = lerp(particle.startColor, emitter.colorEnd, age);
    float fadeIn = emitter.life.z > 1e-5 ? smoothstep(0.0, emitter.life.z, age) : 1.0;
    float fadeOut = emitter.life.w > 1e-5 ? 1.0 - smoothstep(1.0 - emitter.life.w, 1.0, age) : 1.0;
    color.a *= fadeIn * fadeOut;
    particle.color = color;
}

// ── the behavior contract ───────────────────────────────────────────────────

// The simulation behavior of a 3D particle group; the 3D counterpart of
// IParticleBehavior2D (AlcoParticles_Core2D) — see its documentation for the
// custom behavior authoring pattern.
public interface IParticleBehavior3D
{
    // Initializes a freshly spawned particle. `rng` is seeded per particle.
    public void spawn(inout ParticleData3D particle, EmitterParams3D emitter, inout uint rng)
    {
        particleSpawnDefault3D(particle, emitter, rng);
    }

    // Advances one live particle by emitter.deltaTime; decrement the lifetime
    // and let it reach <= 0 to kill the particle.
    public void simulate(inout ParticleData3D particle, EmitterParams3D emitter)
    {
        particleSimulateDefault3D(particle, emitter);
    }
}
