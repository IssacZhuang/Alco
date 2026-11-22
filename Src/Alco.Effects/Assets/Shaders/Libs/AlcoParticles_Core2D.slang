// file "AlcoParticles_Core2D.slang" - module "AlcoParticles_Core2D"
//
// The 2D GPU particle contract: the particle record, the per-emitter parameter
// block (CPU-authored every frame), the draw-args record, the hash RNG and the
// simulation behavior interface with full default implementations.
//
// The C# twins of ParticleData2D / EmitterParams2D / GpuDrawArgs (Alco.Effects)
// must match these layouts field for field (row-major matrices, ForceCLayout).

#language slang 2025
module AlcoParticles_Core2D;

import Core;

public static const float PI = 3.14159265359;
public static const float TAU = 6.28318530718;

// ── particle record (92 bytes) ──────────────────────────────────────────────

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
    public float height;           // independent height above the ground plane
    public float heightVelocity;   // height units per second
    public float lifetime;         // remaining seconds; <= 0 marks a dead (free) slot
    public float duration;         // total seconds
    public uint animStart;         // flipbook: first frame of the anim picked at spawn
};

// ── per-emitter parameters (368 bytes, one per emitter slot, CPU-written) ───

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
    public uint flags;             // PARTICLE_FLAG_* bits
    // emitter transform (spawn only, world space, row-major)
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
    public float4 motion;          // xy = planar gravity, z = drag, w = height acceleration
    public float4 heightMotion;    // xy = initial height min/max, zw = height velocity min/max
    public float4 colorMin;
    public float4 colorMax;
    public float4 colorEnd;
    // render
    public float4 tint;
    public float4 flipbook;        // x = rows, y = cols, z = lifetime-relative cycles,
                                   // w = frames per anim (0 = whole sheet, one anim)
    // Tail block (32 bytes, total 368 bytes):
    public uint spriteTextureId;         // bindless sprite Texture2D descriptor index
    public float customData;             // custom per-instance channel
    public float emitterHeight;          // base height added to world-space spawns
    public float depthBase;              // base world z of the group's depth plane (2D only)
    public float2 positionOffset;        // fixed spawn offset in emitter-local space
    public uint colorGradientTextureId;  // bindless 256x1 color gradient Texture2D index
    public uint sizeCurveTextureId;      // bindless 256x1 size curve Texture2D index
};

// ── emitter flag bits ───────────────────────────────────────────────────────
public static const uint PARTICLE_FLAG_WORLD_SPACE = 1u;
public static const uint PARTICLE_FLAG_COLOR_GRADIENT = 2u;
public static const uint PARTICLE_FLAG_SIZE_CURVE = 4u;
public static const uint PARTICLE_FLAG_VELOCITY_STRETCH = 8u;
public static const uint PARTICLE_FLAG_FLIPBOOK_REVERSE = 16u;
public static const uint PARTICLE_FLAG_DEPTH_BASE = 32u;

// ── indirect draw record (16 bytes, non-indexed DrawIndirect) ───────────────

public struct GpuDrawArgs
{
    public uint vertexCount;
    public uint instanceCount;
    public uint firstVertex;
    public uint firstInstance;
};

// ── per-draw instance record (8 bytes, one per live particle per frame) ─────

public struct GpuInstanceData
{
    public uint emitterSlot;    // the emitter slot the particle belongs to
    public uint particleIndex;  // the particle's slot inside the emitter's slice
};

// ── batched dispatch work record (16 bytes, one per 64-thread block) ────────

public struct GpuWorkBlock
{
    public uint emitterSlot;   // the emitter slot the block's threads work on
    public uint drawIndex;     // the batch's record slot in this frame's draw-args array
    public uint drawStart;     // the batch's base index inside the instance-data buffer
    public uint threadBase;    // the block's first thread index within the group's span
};

// ── procedural centered quad (vertexId 0..5, clockwise front face) ─────────

public void particleQuadVertex(uint vertexId, out float2 cornerPos, out float2 uv)
{
    switch (vertexId)
    {
    case 0u: cornerPos = float2(-0.5,  0.5); uv = float2(0.0, 0.0); break;
    case 1u: cornerPos = float2( 0.5,  0.5); uv = float2(1.0, 0.0); break;
    case 2u: cornerPos = float2( 0.5, -0.5); uv = float2(1.0, 1.0); break;
    case 3u: cornerPos = float2(-0.5,  0.5); uv = float2(0.0, 0.0); break;
    case 4u: cornerPos = float2( 0.5, -0.5); uv = float2(1.0, 1.0); break;
    default: cornerPos = float2(-0.5, -0.5); uv = float2(0.0, 1.0); break;
    }
}

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

public uint particlePickFlipbookAnim(float4 flipbook, inout uint state)
{
    if (flipbook.w < 0.5)
    {
        return 0u;
    }
    uint total = uint(flipbook.x) * uint(flipbook.y);
    uint perAnim = min(uint(flipbook.w), total);
    uint animCount = max(total / perAnim, 1u);
    return min(uint(particleRandom(state) * float(animCount)), animCount - 1u) * perAnim;
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

public void particleSpawnDefault2D(inout ParticleData2D particle, EmitterParams2D emitter, inout uint rng)
{
    float2 localPosition = particleSampleShape2D(emitter, rng);
    localPosition += emitter.positionOffset;
    float2 velocity = particleSampleVelocity2D(emitter, localPosition, rng);
    float2 position = localPosition;
    if ((emitter.flags & PARTICLE_FLAG_WORLD_SPACE) != 0u)
    {
        position = mul(float4(localPosition, 0.0, 1.0), emitter.worldMatrix).xy;
        velocity = mul(float4(velocity, 0.0, 0.0), emitter.worldMatrix).xy;
    }
    particle.position = position;
    particle.velocity = velocity;
    particle.height = particleRange(rng, emitter.heightMotion.x, emitter.heightMotion.y);
    particle.heightVelocity = particleRange(rng, emitter.heightMotion.z, emitter.heightMotion.w);
    if ((emitter.flags & PARTICLE_FLAG_WORLD_SPACE) != 0u)
    {
        particle.height += emitter.emitterHeight;
    }
    particle.duration = particleRange(rng, emitter.life.x, emitter.life.y);
    particle.lifetime = particle.duration;
    particle.startScale = float2(
        particleRange(rng, emitter.size.x, emitter.size.z),
        particleRange(rng, emitter.size.y, emitter.size.w));
    particle.scale = particle.startScale;
    particle.rotation = particleRange(rng, emitter.rotation2.x, emitter.rotation2.y);
    if (emitter.speed.z > 0.5)
    {
        particle.rotation = atan2(velocity.y, velocity.x);
    }
    particle.angularVelocity = particleRange(rng, emitter.rotation2.z, emitter.rotation2.w);
    particle.startColor = float4(
        particleRandom(rng), particleRandom(rng), particleRandom(rng), particleRandom(rng))
        * (emitter.colorMax - emitter.colorMin) + emitter.colorMin;
    particle.color = particle.startColor;
    particle.animStart = particlePickFlipbookAnim(emitter.flipbook, rng);
}

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
    particle.heightVelocity += emitter.motion.w * dt;
    particle.heightVelocity *= exp(-emitter.motion.z * dt);
    particle.height += particle.heightVelocity * dt;
    particle.rotation += particle.angularVelocity * dt;
    particle.scale = particle.startScale * lerp(1.0, emitter.overLife.x, age);
    float4 color = lerp(particle.startColor, emitter.colorEnd, age);
    float fadeIn = emitter.life.z > 1e-5 ? smoothstep(0.0, emitter.life.z, age) : 1.0;
    float fadeOut = emitter.life.w > 1e-5 ? 1.0 - smoothstep(1.0 - emitter.life.w, 1.0, age) : 1.0;
    color.a *= fadeIn * fadeOut;
    particle.color = color;
}

// ── the behavior contract ───────────────────────────────────────────────────

public interface IParticleBehavior2D
{
    public void spawn(inout ParticleData2D particle, EmitterParams2D emitter, inout uint rng)
    {
        particleSpawnDefault2D(particle, emitter, rng);
    }

    public void simulate(inout ParticleData2D particle, EmitterParams2D emitter)
    {
        particleSimulateDefault2D(particle, emitter);
    }
}
