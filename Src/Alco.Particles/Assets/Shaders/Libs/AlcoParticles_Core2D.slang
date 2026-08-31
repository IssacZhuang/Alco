// file "AlcoParticles_Core2D.slang" - module "AlcoParticles_Core2D"
//
// The 2D GPU particle contract: the particle record, the per-emitter parameter
// block (CPU-authored every frame), the draw-args record, the hash RNG and the
// simulation behavior interface with full default implementations. Custom
// behavior modules export exactly one struct implementing IParticleBehavior2D
// and override only the pieces they change; the emit/simulate pass templates
// compose with the module through slang link-time specialization.
//
// The C# twins of ParticleData2D / EmitterParams2D / GpuDrawArgs (Alco.Particles)
// must match these layouts field for field.

#language slang 2025
module AlcoParticles_Core2D;

import AlcoRendering_Core;

// ── particle record (80 bytes) ──────────────────────────────────────────────

public struct ParticleData2D
{
    public float2 position;        // world (or emitter-local) position
    public float2 scale;           // current quad extents
    public float4 color;           // current color (color-over-life and fades applied)
    public float4 startColor;      // spawn color, the lerp source of color-over-life
    public float2 velocity;
    public float2 startScale;      // spawn scale, the lerp source of size-over-life
    public float rotation;         // radians
    public float angularVelocity;  // radians per second
    public float lifetime;         // remaining seconds; <= 0 marks a dead (free) slot
    public float duration;         // total seconds
};

// ── per-emitter parameters (336 bytes, one per emitter slot, CPU-written) ───

public struct EmitterParams2D
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
                                   // bit2: size curve lookup, bit3: velocity-stretched quads,
                                   // bit4: flipbook plays in reverse
    // emitter transform (spawn only, world space)
    public float4x4 worldMatrix;
    // emission shape: x = type (0 point, 1 circle, 2 box), y = radius,
    // z = hollow inner radius fraction [0, 1]
    public float4 shape;
    public float4 extents;         // xy = box half extents
    // emission direction: xy = base direction, z = spread half-angle (rad),
    // w = direction mode (0 constant, 1 radial)
    public float4 emission;
    public float4 speed;           // x = min, y = max, z = align rotation to velocity, w = stretch speed scale
    // life: x = lifetime min, y = lifetime max, z = fade-in fraction, w = fade-out fraction
    public float4 life;
    public float4 size;            // xy = min, zw = max (quad extents at spawn)
    public float4 rotation2;       // x = start rotation min, y = max, z = angular velocity min, w = max
    public float4 overLife;        // x = end scale multiplier, y = stretch length scale
    public float4 motion;          // xy = gravity, z = drag
    public float4 colorMin;
    public float4 colorMax;
    public float4 colorEnd;
    // render
    public float4 tint;
    public float4 flipbook;        // x = rows, y = cols, z = fps, w = loop (0/1)
    public uint4 renderMisc;       // x = quad mesh index count, y = custom per-instance
                                   // data (float bits; surfaced to material vertex hooks
                                   // as ParticleVertexInput.customData)
};

// ── emitter flag bits ───────────────────────────────────────────────────────
// Shared by the 2D and 3D emitter parameter blocks (same bit positions); the
// C# twins mirror them as EmitterParams2D.Flag*/EmitterParams3D.Flag*.
public static const uint PARTICLE_FLAG_WORLD_SPACE = 1u;       // world-space simulation
public static const uint PARTICLE_FLAG_COLOR_GRADIENT = 2u;    // color-gradient lookup bound
public static const uint PARTICLE_FLAG_SIZE_CURVE = 4u;        // size-curve lookup bound
public static const uint PARTICLE_FLAG_VELOCITY_STRETCH = 8u;  // velocity-stretched quads
public static const uint PARTICLE_FLAG_FLIPBOOK_REVERSE = 16u; // flipbook plays in reverse

// ── indirect draw record (matches Alco.Graphics.IndexedIndirectData) ────────

public struct GpuDrawArgs
{
    public uint indexCount;
    public uint instanceCount;
    public uint firstIndex;
    public uint vertexOffset;
    public uint firstInstance;
};

// ── hash RNG (PCG) ──────────────────────────────────────────────────────────

public uint particleHash(uint seed, uint slot)
{
    uint state = seed * 747796405u + slot * 2891336453u + 1013904223u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

public uint particlePcg(inout uint state)
{
    state = state * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

// Uniform sample in [0, 1).
public float particleRandom(inout uint state)
{
    return float(particlePcg(state)) * (1.0 / 4294967296.0);
}

public float particleRange(inout uint state, float minValue, float maxValue)
{
    return lerp(minValue, maxValue, particleRandom(state));
}

// ── small math helpers ──────────────────────────────────────────────────────

// Rotates a 2D vector by an angle in radians (left-handed).
public float2 particleRotate(float2 v, float radians)
{
    float s = sin(radians);
    float c = cos(radians);
    return float2(c * v.x - s * v.y, s * v.x + c * v.y);
}

public float2 particleNormalizeSafe(float2 v, float2 fallback)
{
    float lengthSquared = dot(v, v);
    return lengthSquared > 1e-8 ? v * rsqrt(lengthSquared) : fallback;
}

// ── default spawn/simulate pieces (reusable by custom behaviors) ────────────

// Samples the emission shape in emitter-local space.
public float2 particleSampleShape2D(EmitterParams2D emitter, inout uint rng)
{
    uint type = uint(emitter.shape.x);
    if (type == 1u) // circle
    {
        float angle = particleRandom(rng) * TAU;
        float inner = clamp(emitter.shape.z, 0.0, 1.0);
        float radius = emitter.shape.y * sqrt(lerp(inner * inner, 1.0, particleRandom(rng)));
        return float2(cos(angle), sin(angle)) * radius;
    }
    if (type == 2u) // box
    {
        return (float2(particleRandom(rng), particleRandom(rng)) * 2.0 - 1.0) * emitter.extents.xy;
    }
    return float2(0.0, 0.0);
}

// Picks the initial velocity from the emission direction parameters.
public float2 particleSampleVelocity2D(EmitterParams2D emitter, float2 localPosition, inout uint rng)
{
    float2 direction;
    if (emitter.emission.w > 0.5) // radial
    {
        direction = particleNormalizeSafe(localPosition, particleNormalizeSafe(emitter.emission.xy, float2(0.0, 1.0)));
    }
    else
    {
        float spread = (particleRandom(rng) * 2.0 - 1.0) * emitter.emission.z;
        direction = particleRotate(particleNormalizeSafe(emitter.emission.xy, float2(0.0, 1.0)), spread);
    }
    return direction * particleRange(rng, emitter.speed.x, emitter.speed.y);
}

// The default spawn: shape sample, velocity pick, lifetime/size/rotation/color
// ranges, optional transform into world space.
public void particleSpawnDefault2D(inout ParticleData2D particle, EmitterParams2D emitter, inout uint rng)
{
    float2 localPosition = particleSampleShape2D(emitter, rng);
    float2 velocity = particleSampleVelocity2D(emitter, localPosition, rng);
    float2 position = localPosition;
    if ((emitter.flags & PARTICLE_FLAG_WORLD_SPACE) != 0u) // world space: spawn through the emitter transform
    {
        position = mul(emitter.worldMatrix, float4(localPosition, 0.0, 1.0)).xy;
        velocity = mul(emitter.worldMatrix, float4(velocity, 0.0, 0.0)).xy;
    }
    particle.position = position;
    particle.velocity = velocity;
    particle.duration = particleRange(rng, emitter.life.x, emitter.life.y);
    particle.lifetime = particle.duration;
    particle.startScale = float2(
        particleRange(rng, emitter.size.x, emitter.size.z),
        particleRange(rng, emitter.size.y, emitter.size.w));
    particle.scale = particle.startScale;
    particle.rotation = particleRange(rng, emitter.rotation2.x, emitter.rotation2.y);
    if (emitter.speed.z > 0.5) // align rotation to velocity
    {
        particle.rotation = atan2(velocity.y, velocity.x);
    }
    particle.angularVelocity = particleRange(rng, emitter.rotation2.z, emitter.rotation2.w);
    particle.startColor = float4(
        particleRandom(rng), particleRandom(rng), particleRandom(rng), particleRandom(rng))
        * (emitter.colorMax - emitter.colorMin) + emitter.colorMin;
    particle.color = particle.startColor;
}

// The default simulate: gravity, exponential drag, integration, rotation,
// color/size over life, fade in/out. Dead particles keep their state except the
// lifetime (the slot is free once lifetime <= 0).
public void particleSimulateDefault2D(inout ParticleData2D particle, EmitterParams2D emitter)
{
    float dt = emitter.deltaTime;
    particle.lifetime -= dt;
    if (particle.lifetime <= 0.0)
    {
        return;
    }
    float age = 1.0 - particle.lifetime / max(particle.duration, 1e-5);
    particle.velocity += emitter.motion.xy * dt;
    particle.velocity *= exp(-emitter.motion.z * dt);
    particle.position += particle.velocity * dt;
    particle.rotation += particle.angularVelocity * dt;
    particle.scale = particle.startScale * lerp(1.0, emitter.overLife.x, age);
    float4 color = lerp(particle.startColor, emitter.colorEnd, age);
    float fadeIn = emitter.life.z > 1e-5 ? smoothstep(0.0, emitter.life.z, age) : 1.0;
    float fadeOut = emitter.life.w > 1e-5 ? 1.0 - smoothstep(1.0 - emitter.life.w, 1.0, age) : 1.0;
    color.a *= fadeIn * fadeOut;
    particle.color = color;
}

// ── the behavior contract ───────────────────────────────────────────────────

// The simulation behavior of a 2D particle group. Both methods carry full
// defaults; a custom behavior module exports exactly one public struct
// implementing this interface and overrides only what it changes:
//
//   import AlcoParticles_Core2D;
//   public struct MyBehavior : IParticleBehavior2D
//   {
//       public override void simulate(inout ParticleData2D particle, EmitterParams2D emitter) { ... }
//   }
public interface IParticleBehavior2D
{
    // Initializes a freshly spawned particle. `rng` is seeded per particle.
    public void spawn(inout ParticleData2D particle, EmitterParams2D emitter, inout uint rng)
    {
        particleSpawnDefault2D(particle, emitter, rng);
    }

    // Advances one live particle by emitter.deltaTime; decrement the lifetime
    // and let it reach <= 0 to kill the particle.
    public void simulate(inout ParticleData2D particle, EmitterParams2D emitter)
    {
        particleSimulateDefault2D(particle, emitter);
    }
}
