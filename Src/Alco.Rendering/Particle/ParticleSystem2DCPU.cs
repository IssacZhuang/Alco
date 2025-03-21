namespace Alco.Rendering;

using System;

public class ParticleSystem2DCPU : AutoDisposable
{
    private NativeArrayList<ParticleData2D> _particles;//simulate on cpu

    private IParticleEmitter<ParticleData2D> _emitter;
    public IParticleEmitter<ParticleData2D> Emitter
    {
        get => _emitter;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _emitter = value;
        }
    }

    /// <summary>
    /// Whether the particle system is currently playing
    /// </summary>
    public bool IsPlaying { get; private set; }

    /// <summary>
    /// Maximum number of particles this system can handle
    /// </summary>
    public int MaxParticles { get; set; } = 1000;

    /// <summary>
    /// Whether the particle system is in burst mode
    /// </summary>
    public bool IsBurst { get; set; } = false;

    /// <summary>
    /// Whether the particle system should loop
    /// </summary>
    public bool IsLooping { get; set; } = true;

    /// <summary>
    /// Rate at which particles are emitted (particles per second)
    /// <br/>[Attention] This value is only used when IsBurst is false
    /// </summary>
    public float EmissionRateOverTime { get; set; } = 10f;

    /// <summary>
    /// Minimum number of particles to emit in a burst
    /// <br/>[Attention] This value is only used when IsBurst is true
    /// </summary>
    public uint MinBurstCount { get; set; } = 10;

    /// <summary>
    /// Maximum number of particles to emit in a burst
    /// <br/>[Attention] This value is only used when IsBurst is true
    /// </summary>
    public uint MaxBurstCount { get; set; } = 20;

    /// <summary>
    /// Default lifetime for particles in seconds
    /// </summary>
    public float ParticleLifetime { get; set; } = 5f;


    public ParticleSystem2DCPU(IParticleEmitter<ParticleData2D> emitter)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        _emitter = emitter;
        _particles = new NativeArrayList<ParticleData2D>(32);
    }


    public void Play()
    {
        if (IsPlaying)
            return;

        IsPlaying = true;

        if (IsBurst)
        {
            // For burst mode, emit particles immediately
            uint burstCount = MinBurstCount;
            if (MaxBurstCount > MinBurstCount)
            {
                burstCount += (uint)Random.Shared.Next(0, (int)(MaxBurstCount - MinBurstCount + 1));
            }

            for (uint i = 0; i < burstCount && _particles.Length < MaxParticles; i++)
            {
                var particle = _emitter.Emit();
                particle.Lifetime = ParticleLifetime;
                _particles.Add(particle);
            }
        }
    }

    public void Stop()
    {
        IsPlaying = false;
    }

    private float _emitAccumulator = 0f;

    public void Update(float deltaTime)
    {
        if (!IsPlaying)
            return;

        // Update existing particles
        for (int i = _particles.Length - 1; i >= 0; i--)
        {
            var particle = _particles[i];

            // Update lifetime
            particle.Lifetime -= deltaTime;

            if (particle.Lifetime <= 0)
            {
                // Remove dead particles using unordered removal (swap with last element)
                int lastIndex = _particles.Length - 1;
                if (i != lastIndex)
                {
                    _particles[i] = _particles[lastIndex];
                }
                // Remove last element
                _particles.RemoveAt(lastIndex);
                continue;
            }

            // Update position based on velocity
            particle.Position += particle.Velocity * deltaTime;

            // Store updated particle
            _particles[i] = particle;
        }

        // Emit new particles over time (only in continuous mode)
        if (!IsBurst)
        {
            _emitAccumulator += deltaTime;
            float emissionInterval = 1.0f / EmissionRateOverTime;

            while (_emitAccumulator >= emissionInterval && _particles.Length < MaxParticles)
            {
                var particle = _emitter.Emit();
                particle.Lifetime = ParticleLifetime;
                _particles.Add(particle);

                _emitAccumulator -= emissionInterval;
            }

            // Stop if not looping and we've finished emitting
            if (!IsLooping && _particles.Length == 0)
            {
                Stop();
            }
        }
        // For burst mode, if not looping and all particles are dead, stop the system
        else if (!IsLooping && _particles.Length == 0)
        {
            Stop();
        }
    }

    protected override void Dispose(bool disposing)
    {
        _particles.Dispose();
    }
}

