using Alco;

namespace Alco.Particles;

/// <summary>
/// The CPU-side emission bookkeeping shared by the 2D and 3D particle systems:
/// advances one emitter group's timeline and computes how many particles it
/// spawns this frame (continuous rate + bursts). Deterministic per instance
/// (seeded <see cref="FastRandom"/>), which keeps screenshot runs reproducible.
/// </summary>
internal static class ParticleEmission
{
    /// <summary>
    /// Advances the emission timeline by one frame.
    /// </summary>
    /// <param name="time">The unwrapped timeline position in seconds (never wraps; the shader-side emitter time wraps by modulo).</param>
    /// <param name="accumulator">The fractional-particle accumulator of the continuous rate.</param>
    /// <param name="deltaTime">The frame's delta time in seconds.</param>
    /// <param name="rate">The continuous emission rate in particles per second.</param>
    /// <param name="duration">The timeline length in seconds; 0 = infinite.</param>
    /// <param name="looping">Whether the timeline wraps at <paramref name="duration"/>.</param>
    /// <param name="bursts">The burst table.</param>
    /// <param name="random">The instance's RNG (burst counts only).</param>
    /// <param name="capacity">The emitter slice capacity; the spawn count clamps to it (ring overwrite).</param>
    /// <returns>The number of particles to spawn this frame.</returns>
    public static uint Advance(
        ref float time,
        ref float accumulator,
        float deltaTime,
        float rate,
        float duration,
        bool looping,
        IReadOnlyList<ParticleBurst> bursts,
        ref FastRandom random,
        uint capacity)
    {
        float previousTime = time;
        time += deltaTime;

        bool emitting = IsEmitting(time, duration, looping);
        uint spawn = 0;
        if (emitting && rate > 0f)
        {
            // The clamp bounds the catch-up burst after a hitch to ~1 s of emission.
            accumulator = Math.Min(accumulator + rate * deltaTime, Math.Max(rate, 1f));
            uint count = (uint)accumulator;
            accumulator -= count;
            spawn += count;
        }

        // Bursts fire at burst.Time + k * duration on the unwrapped timeline;
        // the half-open window [previous, now) fires each exactly once per cycle.
        for (int i = 0; i < bursts.Count; i++)
        {
            ParticleBurst burst = bursts[i];
            if (burst.Time < 0f || burst.CountMax <= 0)
            {
                continue;
            }
            if (duration > 0f && looping)
            {
                if (burst.Time >= duration)
                {
                    continue;
                }
                int cycleNow = (int)(time / duration);
                int cyclePrev = (int)(previousTime / duration);
                for (int cycle = cyclePrev; cycle <= cycleNow; cycle++)
                {
                    float fireTime = burst.Time + cycle * duration;
                    if (fireTime >= previousTime && fireTime < time)
                    {
                        spawn += (uint)random.NextInt(burst.CountMin, burst.CountMax + 1);
                    }
                }
            }
            else
            {
                if (burst.Time >= previousTime && burst.Time < time && (duration <= 0f || burst.Time <= duration))
                {
                    spawn += (uint)random.NextInt(burst.CountMin, burst.CountMax + 1);
                }
            }
        }
        return Math.Min(spawn, capacity);
    }

    /// <summary>
    /// Whether an emitter is still on its emission timeline: infinite timelines
    /// (<paramref name="duration"/> 0) and looping timelines emit forever; a
    /// non-looping timeline ends at <paramref name="duration"/>.
    /// </summary>
    /// <param name="time">The unwrapped timeline position in seconds.</param>
    /// <param name="duration">The timeline length in seconds; 0 = infinite.</param>
    /// <param name="looping">Whether the timeline wraps at <paramref name="duration"/>.</param>
    public static bool IsEmitting(float time, float duration, bool looping)
        => duration <= 0f || looping || time < duration;

    /// <summary>
    /// Advances one emitter group's lifecycle by a frame: while the group is playing
    /// AND on its emission timeline, spawns via <see cref="Advance"/> and resets the
    /// idle timer; otherwise (stopped, or a one-shot timeline that ran out) the idle
    /// timer accumulates. A group past its emission stays active until every live
    /// particle had time to die — this is what lets one-shot effects deactivate and
    /// be destroyed instead of lingering forever.
    /// </summary>
    /// <param name="time">The unwrapped timeline position in seconds.</param>
    /// <param name="accumulator">The fractional-particle accumulator of the continuous rate.</param>
    /// <param name="idleTimer">The time since emission stopped (drives deactivation).</param>
    /// <param name="deltaTime">The frame's delta time in seconds.</param>
    /// <param name="playing">Whether the owning instance is playing (not stopped).</param>
    /// <param name="rate">The continuous emission rate in particles per second.</param>
    /// <param name="duration">The timeline length in seconds; 0 = infinite.</param>
    /// <param name="looping">Whether the timeline wraps at <paramref name="duration"/>.</param>
    /// <param name="bursts">The burst table.</param>
    /// <param name="random">The instance's RNG (burst counts only).</param>
    /// <param name="capacity">The emitter slice capacity; the spawn count clamps to it (ring overwrite).</param>
    /// <param name="maxLifetime">The maximum particle lifetime in seconds.</param>
    /// <param name="active">Whether the group still does GPU work (emitting or has live particles).</param>
    /// <returns>The number of particles to spawn this frame.</returns>
    public static uint AdvanceLifecycle(
        ref float time,
        ref float accumulator,
        ref float idleTimer,
        float deltaTime,
        bool playing,
        float rate,
        float duration,
        bool looping,
        IReadOnlyList<ParticleBurst> bursts,
        ref FastRandom random,
        uint capacity,
        float maxLifetime,
        out bool active)
    {
        bool emitting = playing && IsEmitting(time, duration, looping);
        uint spawn = 0;
        if (emitting)
        {
            spawn = Advance(ref time, ref accumulator, deltaTime, rate, duration, looping, bursts, ref random, capacity);
            idleTimer = 0f;
        }
        else
        {
            idleTimer += deltaTime;
        }
        active = emitting || idleTimer <= maxLifetime + 0.1f;
        return spawn;
    }
}
