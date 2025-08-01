namespace Alco.Rendering;

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using static Alco.math;

/// <summary>
/// A CPU-based 2D particle system that handles simulation and rendering of particles.
/// </summary>
public sealed unsafe class ParticleSystem2DCPU
{
    public const string ShaderDefine_SPACE_MODE_WORLD = "SPACE_MODE_WORLD";

    /// <summary>
    /// Default implementation of particle simulator that updates particle position based on velocity.
    /// </summary>
    public sealed class DefaultSimulator : IParticleSimulator2D
    {
        /// <summary>
        /// Simulates a particle by updating its position based on velocity and reducing its lifetime.
        /// </summary>
        /// <param name="system">The particle system managing this particle.</param>
        /// <param name="particle">Reference to the particle being simulated.</param>
        /// <param name="deltaTime">Time elapsed since the last simulation step.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SimulateInLocal(ParticleSystem2DCPU system, ref ParticleData2D particle, float deltaTime)
        {
            particle.Position += particle.Velocity * deltaTime;
            particle.Lifetime -= deltaTime;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SimulateInWorld(ParticleSystem2DCPU system, ref ParticleData2D particle, float deltaTime)
        {
            particle.Position += particle.Velocity * deltaTime;
            particle.Lifetime -= deltaTime;
        }
    }

    private const int GPUBufferBlockSize = 64 * 1024;
    private static readonly int MaxParticlePerBuffer = GPUBufferBlockSize / sizeof(ParticleData2D);
    private readonly ArrayBuffer<ParticleData2D> _particles;//simulate on cpu
    private SpaceMode _spaceMode = SpaceMode.Local;

    private IParticleSimulator2D _simulator;

    private IParticleEmitter2D _emitter;
    /// <summary>
    /// Gets or sets the particle emitter used to generate new particles.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when attempting to set a null emitter.</exception>
    public IParticleEmitter2D Emitter
    {
        get => _emitter;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _emitter = value;
        }
    }

    /// <summary>
    /// Gets or sets the particle simulator used to update particle behavior.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when attempting to set a null simulator.</exception>
    public IParticleSimulator2D Simulator
    {
        get => _simulator;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _simulator = value;
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
    public int MinBurstCount { get; set; } = 10;

    /// <summary>
    /// Maximum number of particles to emit in a burst
    /// <br/>[Attention] This value is only used when IsBurst is true
    /// </summary>
    public int MaxBurstCount { get; set; } = 20;

    /// <summary>
    /// Default lifetime for particles in seconds
    /// </summary>
    public float ParticleLifetime { get; set; } = 5f;

    /// <summary>
    /// The transform of the particle system
    /// </summary>
    public Transform2D Transform = Transform2D.Identity;

    /// <summary>
    /// The space mode of the particle system.
    /// </summary>
    public SpaceMode SpaceMode
    {
        get => _spaceMode;
        set
        {
            if (_spaceMode != value)
            {
                SwitchSpaceMode(value);
            }
        }
    }

    public Span<ParticleData2D> Particles => _particles.AsSpan();

    public ParticleSystem2DCPU(
        IParticleEmitter2D emitter,
        IParticleSimulator2D? simulator = null)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        _emitter = emitter;
        _simulator = simulator ?? new DefaultSimulator();
        _particles = new ArrayBuffer<ParticleData2D>();
    }

    /// <summary>
    /// Starts playing the particle system. If already playing and in burst mode, triggers another burst.
    /// </summary>
    public void Play()
    {
        if (IsPlaying)
        {
            if (IsBurst)
            {
                DoBurst();
            }
            return;
        }

        IsPlaying = true;
    }

    /// <summary>
    /// Stops the particle system from playing.
    /// </summary>
    public void Stop()
    {
        IsPlaying = false;
    }

    private float _emitAccumulator = 0f;

    private void DoBurst()
    {
        int burstCount = MinBurstCount;
        if (MaxBurstCount > MinBurstCount)
        {
            burstCount += Random.Shared.Next(0, MaxBurstCount - MinBurstCount + 1);
        }

        if (_spaceMode == SpaceMode.Local)
        {
            // for (uint i = 0; i < burstCount && _particles.Length < MaxParticles; i++)
            // {
            //     var particle = _emitter.EmitInLocal();
            //     particle.Lifetime = ParticleLifetime;
            //     _particles.Add(particle);
            // }
            int newSize = math.min(MaxParticles, _particles.Length + burstCount);
            int spanStart = _particles.Length;
            _particles.SetSize(newSize);
            Span<ParticleData2D> particles = _particles.AsSpan(spanStart, burstCount);
            for (int i = 0; i < burstCount; i++)
            {
                particles[i] = _emitter.EmitInLocal();
                particles[i].Lifetime = ParticleLifetime;
            }
        }
        else
        {
            int newSize = math.min(MaxParticles, _particles.Length + burstCount);
            int spanStart = _particles.Length;
            _particles.SetSize(newSize);
            Span<ParticleData2D> particles = _particles.AsSpan(spanStart, burstCount);
            for (int i = 0; i < burstCount; i++)
            {
                particles[i] = _emitter.EmitInWorld(Transform);
                particles[i].Lifetime = ParticleLifetime;
            }
        }
    }

    /// <summary>
    /// Simulates all active particles in the system for the given time step.
    /// Dead particles (lifetime <= 0) are swapped to the end of the active range and then removed efficiently.
    /// Uses an activeLength counter to prevent re-checking already processed dead particles.
    /// </summary>
    /// <param name="deltaTime">Time elapsed since the last simulation step in seconds.</param>
    public void Simulate(float deltaTime)
    {
        if (!IsPlaying)
            return;

        Span<ParticleData2D> particles = _particles.AsSpan();
        int removeCount = 0;

        // Update existing particles
        if (_spaceMode == SpaceMode.Local)
        {
            int activeLength = particles.Length;
            for (int i = 0; i < activeLength; i++)
            {
                ref ParticleData2D particle = ref particles[i];

                _simulator.SimulateInLocal(this, ref particle, deltaTime);

                if (particle.Lifetime <= 0)
                {
                    // Swap dead particle with the last active element
                    activeLength--;
                    if (i != activeLength)
                    {
                        (particles[i], particles[activeLength]) = (particles[activeLength], particles[i]);
                    }
                    removeCount++;
                    i--; // Re-check the swapped particle at current position
                }
            }

            // Reduce span size by removing dead particles
            if (removeCount > 0)
            {
                _particles.SetSize(_particles.Length - removeCount);
            }
        }
        else
        {
            int activeLength = particles.Length;
            for (int i = 0; i < activeLength; i++)
            {
                ref ParticleData2D particle = ref particles[i];

                _simulator.SimulateInWorld(this, ref particle, deltaTime);

                if (particle.Lifetime <= 0)
                {
                    // Swap dead particle with the last active element
                    activeLength--;
                    if (i != activeLength)
                    {
                        (particles[i], particles[activeLength]) = (particles[activeLength], particles[i]);
                    }
                    removeCount++;
                    i--; // Re-check the swapped particle at current position
                }
            }

            // Reduce span size by removing dead particles
            if (removeCount > 0)
            {
                _particles.SetSize(_particles.Length - removeCount);
            }
        }


        // Emit new particles over time (only in continuous mode)
        if (!IsBurst)
        {
            _emitAccumulator += deltaTime;
            float emissionInterval = 1.0f / EmissionRateOverTime;

            // Calculate how many particles to emit using division
            int particlesToEmit = (int)(_emitAccumulator / emissionInterval);
            int maxPossibleEmit = MaxParticles - _particles.Length;
            particlesToEmit = min(particlesToEmit, maxPossibleEmit);

            if (particlesToEmit > 0)
            {
                // Batch expand the ArrayBuffer
                int currentSize = _particles.Length;
                int newSize = currentSize + particlesToEmit;
                _particles.SetSize(newSize);
                Span<ParticleData2D> newParticles = _particles.AsSpan(currentSize, particlesToEmit);

                if (SpaceMode == SpaceMode.Local)
                {
                    for (int i = 0; i < particlesToEmit; i++)
                    {
                        newParticles[i] = _emitter.EmitInLocal();
                        newParticles[i].Lifetime = ParticleLifetime;
                    }
                }
                else
                {
                    for (int i = 0; i < particlesToEmit; i++)
                    {
                        newParticles[i] = _emitter.EmitInWorld(Transform);
                        newParticles[i].Lifetime = ParticleLifetime;
                    }
                }

                // Update accumulator once after emitting all particles
                _emitAccumulator -= particlesToEmit * emissionInterval;
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


    /// <summary>
    /// Clear all active particles.
    /// </summary>
    public void Clear()
    {
        _particles.Clear();
    }


    private void SwitchSpaceMode(SpaceMode mode)
    {
        _spaceMode = mode;
        Clear();
    }
}

