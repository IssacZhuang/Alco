namespace Alco.Rendering;

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

/// <summary>
/// A CPU-based 2D particle system that handles simulation and rendering of particles.
/// </summary>
public sealed unsafe class ParticleSystem2DCPU : AutoDisposable
{
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
        public void Simulate(ParticleSystem2DCPU system, ref ParticleData2D particle, float deltaTime)
        {
            particle.Position += particle.Velocity * deltaTime;
            particle.Lifetime -= deltaTime;
        }
    }

    private const int GPUBufferBlockSize = 64 * 1024;
    private static readonly int MaxParticlePerBuffer = GPUBufferBlockSize / sizeof(ParticleData2D);
    private readonly RenderingSystem _renderingSystem;
    private readonly List<GraphicsBuffer> _buffers = new();
    private readonly Mesh _mesh;
    private readonly Material _material;
    private readonly uint _shaderId_particles;
    private NativeArrayList<ParticleData2D> _particles;//simulate on cpu
    private bool _isParticleDirty = false;

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

    internal ParticleSystem2DCPU(
        RenderingSystem renderingSystem,
        Mesh mesh,
        Material material,
        IParticleEmitter2D emitter,
        IParticleSimulator2D? simulator = null)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        _emitter = emitter;
        _simulator = simulator ?? new DefaultSimulator();
        _renderingSystem = renderingSystem;
        _particles = new NativeArrayList<ParticleData2D>(32);
        _mesh = mesh;
        _material = material.CreateInstance();
        _shaderId_particles = _material.GetResourceId(ShaderResourceId.Particles);
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

        for (uint i = 0; i < burstCount && _particles.Length < MaxParticles; i++)
        {
            var particle = _emitter.Emit();
            particle.Lifetime = ParticleLifetime;
            _particles.Add(particle);
        }
        _isParticleDirty = true;
    }

    /// <summary>
    /// Simulates all active particles in the system for the given time step.
    /// </summary>
    /// <param name="deltaTime">Time elapsed since the last simulation step in seconds.</param>
    public void Simulate(float deltaTime)
    {
        if (!IsPlaying)
            return;

        ParticleData2D* particles = _particles.UnsafePointer;

        // Update existing particles
        for (int i = _particles.Length - 1; i >= 0; i--)
        {
            ref ParticleData2D particle = ref particles[i];

            if (particle.Lifetime <= 0)
            {
                // Remove dead particles using unordered removal (swap with last element)
                int lastIndex = _particles.Length - 1;
                if (i != lastIndex)
                {
                    particle = particles[lastIndex];
                }
                // Remove last element
                _particles.RemoveAt(lastIndex);
                continue;
            }

            OnSimulate(ref particle, deltaTime);
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
        _isParticleDirty = true;
    }

    /// <summary>
    /// Render the particle system.
    /// </summary>
    /// <param name="context">The render context.</param>
    public unsafe void Render(RenderContext context)
    {
        if (!IsPlaying)
        {
            return;
        }

        //update to gpu
        if (_isParticleDirty)
        {
            int bufferRequired = (_particles.Length * sizeof(ParticleData2D) + GPUBufferBlockSize - 1) / GPUBufferBlockSize;
            int bufferNeedToRent = bufferRequired - _buffers.Count;
            if (bufferNeedToRent > 0)
            {
                for (int i = 0; i < bufferNeedToRent; i++)
                {
                    _buffers.Add(_renderingSystem.GraphicsBufferPool.GetBuffer(GPUBufferBlockSize));
                }
            }
            else if (bufferNeedToRent < 0)
            {
                for (int i = 0; i < -bufferNeedToRent; i++)
                {
                    _renderingSystem.GraphicsBufferPool.TryReturnBuffer(_buffers[^1]);
                    _buffers.RemoveAt(_buffers.Count - 1);
                }
            }

            int drawCount = _particles.Length;
            for (int i = 0; i < _buffers.Count; i++)
            {
                GraphicsBuffer buffer = _buffers[i];
                int particleCount = Math.Min(drawCount, MaxParticlePerBuffer);
                drawCount -= particleCount;
                buffer.UpdateBuffer(_particles.ReadOnlySpan.Slice(i * MaxParticlePerBuffer, particleCount));
            }
            _isParticleDirty = false;
        }

        //draw
        int drawCount2 = _particles.Length;
        for (int i = 0; i < _buffers.Count; i++)
        {
            GraphicsBuffer buffer = _buffers[i];
            int particleCount = Math.Min(drawCount2, MaxParticlePerBuffer);
            drawCount2 -= particleCount;
            _material.SetBuffer(_shaderId_particles, buffer);
            context.DrawInstancedWithConstant(_mesh, _material, (uint)particleCount, Transform.Matrix);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnSimulate(ref ParticleData2D particle, float deltaTime)
    {
        _simulator.Simulate(this, ref particle, deltaTime);
    }

    /// <summary>
    /// Disposes resources used by the particle system.
    /// </summary>
    /// <param name="disposing">True if called from Dispose, false if called from finalizer.</param>
    protected override void Dispose(bool disposing)
    {
        _particles.Dispose();
        foreach (var buffer in _buffers)
        {
            _renderingSystem.GraphicsBufferPool.TryReturnBuffer(buffer);
        }
        _buffers.Clear();
    }
}

