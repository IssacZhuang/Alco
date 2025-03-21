namespace Alco.Rendering;

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

public unsafe class ParticleSystem2DCPU : AutoDisposable
{
    public class DefaultSimulator : IParticleSimulator
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Simulate(ref ParticleData2D particle, float deltaTime)
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

    private IParticleSimulator _simulator;

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

    public IParticleSimulator Simulator
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


    internal ParticleSystem2DCPU(
        RenderingSystem renderingSystem,
        Mesh mesh,
        Material material,
        IParticleEmitter<ParticleData2D> emitter,
        IParticleSimulator? simulator = null)
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
            _isParticleDirty = true;
        }
    }

    public void Stop()
    {
        IsPlaying = false;
    }

    private float _emitAccumulator = 0f;

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
    /// <param name="transformMatrix">The position, rotation and scale of the particle system.</param>
    public unsafe void Render(RenderContext context, Matrix4x4 transformMatrix)
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
            context.DrawInstancedWithConstant(_mesh, _material, (uint)particleCount, transformMatrix);
        }

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnSimulate(ref ParticleData2D particle, float deltaTime)
    {
        _simulator.Simulate(ref particle, deltaTime);
    }

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

