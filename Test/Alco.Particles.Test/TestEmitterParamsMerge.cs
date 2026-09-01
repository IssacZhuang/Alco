using System.Numerics;
using NUnit.Framework;

namespace Alco.Particles.Test;

/// <summary>
/// Tests of <see cref="EmitterParams2D.MergeEdited"/> and
/// <see cref="EmitterParams3D.MergeEdited"/>: the merge behind the live
/// parameter editing of a running effect instance
/// (<see cref="ParticleEffectInstance2D.SetGroupParams"/> /
/// <see cref="ParticleEffectInstance3D.SetGroupParams"/>) — the static fields
/// come from the edited record while the slot-bound and per-frame fields keep
/// their live values.
/// </summary>
public class TestEmitterParamsMerge
{
    [Test]
    public void Merge2DKeepsSlotBoundAndPerFrameFields()
    {
        EmitterParams2D live = new()
        {
            SpawnCount = 7,
            EmitCursor = 42,
            Capacity = 2048,
            SliceOffset = 4096,
            DeltaTime = 0.016f,
            EmitterTime = 3.25f,
            FrameSeed = 0xDEADBEEF,
            WorldMatrix = Matrix4x4.CreateTranslation(1f, 2f, 0f),
            EmitterHeight = 1.25f,
            IndexCount = 6,
            Speed = new Vector4(1f, 2f, 0f, 0f),
            Life = new Vector4(0.5f, 1.5f, 0.1f, 0.2f),
            Tint = ColorFloat.White,
        };
        EmitterParams2D edited = new()
        {
            // Garbage in the slot-bound/per-frame fields: must be dropped.
            SpawnCount = uint.MaxValue,
            EmitCursor = uint.MaxValue,
            Capacity = 1,
            SliceOffset = 1,
            DeltaTime = -1f,
            EmitterTime = -1f,
            FrameSeed = 1,
            WorldMatrix = Matrix4x4.CreateTranslation(99f, 99f, 99f),
            EmitterHeight = 99f,
            IndexCount = 3,
            // The static edits that must win.
            Speed = new Vector4(10f, 20f, 1f, 0.15f),
            Life = new Vector4(2f, 4f, 0.1f, 0.2f),
            Size = new Vector4(0.5f, 0.5f, 2f, 2f),
            Motion = new Vector4(0f, -9.8f, 1.5f, 0f),
            HeightMotion = new Vector4(0.25f, 0.75f, 2f, 4f),
            Tint = new ColorFloat(1f, 0f, 0f, 1f),
            Flags = EmitterParams2D.FlagVelocityStretch,
        };

        EmitterParams2D merged = EmitterParams2D.MergeEdited(live, edited);

        Assert.Multiple(() =>
        {
            // Slot-bound and per-frame fields survive the edit.
            Assert.That(merged.SpawnCount, Is.EqualTo(7u));
            Assert.That(merged.EmitCursor, Is.EqualTo(42u));
            Assert.That(merged.Capacity, Is.EqualTo(2048u));
            Assert.That(merged.SliceOffset, Is.EqualTo(4096u));
            Assert.That(merged.DeltaTime, Is.EqualTo(0.016f));
            Assert.That(merged.EmitterTime, Is.EqualTo(3.25f));
            Assert.That(merged.FrameSeed, Is.EqualTo(0xDEADBEEFu));
            Assert.That(merged.WorldMatrix, Is.EqualTo(Matrix4x4.CreateTranslation(1f, 2f, 0f)));
            Assert.That(merged.EmitterHeight, Is.EqualTo(1.25f));
            Assert.That(merged.IndexCount, Is.EqualTo(6u));
            // The edited static fields win.
            Assert.That(merged.Speed, Is.EqualTo(new Vector4(10f, 20f, 1f, 0.15f)));
            Assert.That(merged.Life, Is.EqualTo(new Vector4(2f, 4f, 0.1f, 0.2f)));
            Assert.That(merged.Size, Is.EqualTo(new Vector4(0.5f, 0.5f, 2f, 2f)));
            Assert.That(merged.Motion, Is.EqualTo(new Vector4(0f, -9.8f, 1.5f, 0f)));
            Assert.That(merged.HeightMotion, Is.EqualTo(new Vector4(0.25f, 0.75f, 2f, 4f)));
            Assert.That(merged.Tint, Is.EqualTo(new ColorFloat(1f, 0f, 0f, 1f)));
            Assert.That(merged.Flags, Is.EqualTo(EmitterParams2D.FlagVelocityStretch));
        });
    }

    [Test]
    public void Merge3DKeepsSlotBoundAndPerFrameFields()
    {
        EmitterParams3D live = new()
        {
            SpawnCount = 5,
            EmitCursor = 11,
            Capacity = 1024,
            SliceOffset = 8192,
            DeltaTime = 0.033f,
            EmitterTime = 1.5f,
            FrameSeed = 0x12345678,
            WorldMatrix = Matrix4x4.CreateTranslation(0f, 0f, 3f),
            IndexCount = 6,
            Speed = new Vector4(1f, 2f, 0f, 0f),
        };
        EmitterParams3D edited = new()
        {
            SpawnCount = uint.MaxValue,
            EmitCursor = uint.MaxValue,
            Capacity = 1,
            SliceOffset = 1,
            DeltaTime = -1f,
            EmitterTime = -1f,
            FrameSeed = 1,
            WorldMatrix = Matrix4x4.Identity,
            IndexCount = 3,
            Speed = new Vector4(4f, 10f, 1f, 0.18f),
            Size = new Vector4(0.2f, 0.6f, 3.5f, 1.5f),
            Motion = new Vector4(0f, 0f, -4f, 1.6f),
            Tint = new ColorFloat(0f, 1f, 0f, 1f),
            Flags = EmitterParams3D.FlagVelocityStretch,
        };

        EmitterParams3D merged = EmitterParams3D.MergeEdited(live, edited);

        Assert.Multiple(() =>
        {
            Assert.That(merged.SpawnCount, Is.EqualTo(5u));
            Assert.That(merged.EmitCursor, Is.EqualTo(11u));
            Assert.That(merged.Capacity, Is.EqualTo(1024u));
            Assert.That(merged.SliceOffset, Is.EqualTo(8192u));
            Assert.That(merged.DeltaTime, Is.EqualTo(0.033f));
            Assert.That(merged.EmitterTime, Is.EqualTo(1.5f));
            Assert.That(merged.FrameSeed, Is.EqualTo(0x12345678u));
            Assert.That(merged.WorldMatrix, Is.EqualTo(Matrix4x4.CreateTranslation(0f, 0f, 3f)));
            Assert.That(merged.IndexCount, Is.EqualTo(6u));
            Assert.That(merged.Speed, Is.EqualTo(new Vector4(4f, 10f, 1f, 0.18f)));
            Assert.That(merged.Size, Is.EqualTo(new Vector4(0.2f, 0.6f, 3.5f, 1.5f)));
            Assert.That(merged.Motion, Is.EqualTo(new Vector4(0f, 0f, -4f, 1.6f)));
            Assert.That(merged.Tint, Is.EqualTo(new ColorFloat(0f, 1f, 0f, 1f)));
            Assert.That(merged.Flags, Is.EqualTo(EmitterParams3D.FlagVelocityStretch));
        });
    }
}
