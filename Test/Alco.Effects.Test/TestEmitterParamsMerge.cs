using System.Numerics;
using NUnit.Framework;

namespace Alco.Effects.Test;

/// <summary>Verifies that <see cref="EmitterParams2D.MergeEdited"/> and <see cref="EmitterParams3D.MergeEdited"/> preserve slot, frame, and bindless texture state.</summary>
public class TestEmitterParamsMerge
{
    /// <summary>Tests that 2D parameter merging preserves internal state and applies editable fields.</summary>
    [Test]
    public void MergeEdited2DPreservesSlotAndBindlessIdsWhileApplyingEdits()
    {
        var live = new EmitterParams2D
        {
            SpawnCount = 12,
            EmitCursor = 34,
            Capacity = 1024,
            SliceOffset = 512,
            DeltaTime = 0.016f,
            EmitterTime = 3.5f,
            FrameSeed = 0xCAFEBABEu,
            WorldMatrix = Matrix4x4.CreateTranslation(10f, 20f, 0f),
            EmitterHeight = 2.5f,
            DepthBase = -0.25f,
            SpriteTextureId = 77u,
            ColorGradientTextureId = 88u,
            SizeCurveTextureId = 99u,
            Flags = EmitterParams2D.FlagWorldSpace,
            Life = new Vector4(1f, 2f, 0.1f, 0.2f),
            Speed = new Vector4(5f, 10f, 0f, 0f),
            Tint = ColorFloat.White,
        };

        var edited = new EmitterParams2D
        {
            SpawnCount = 999,
            EmitCursor = 999,
            Capacity = 1,
            SliceOffset = 1,
            DeltaTime = 999f,
            EmitterTime = 999f,
            FrameSeed = 1u,
            WorldMatrix = Matrix4x4.Identity,
            EmitterHeight = 0f,
            DepthBase = 0f,
            SpriteTextureId = 0u,
            ColorGradientTextureId = 0u,
            SizeCurveTextureId = 0u,
            Flags = EmitterParams2D.FlagVelocityStretch | EmitterParams2D.FlagColorGradient,
            Life = new Vector4(0.5f, 1.5f, 0.05f, 0.4f),
            Speed = new Vector4(12f, 24f, 1f, 0.5f),
            Size = new Vector4(1f, 1f, 2f, 2f),
            Motion = new Vector4(0f, -9.8f, 0.3f, 0f),
            Tint = new ColorFloat(1f, 0.5f, 0.25f, 0.8f),
        };

        EmitterParams2D merged = EmitterParams2D.MergeEdited(in live, in edited);

        Assert.Multiple(() =>
        {
            Assert.That(merged.SpawnCount, Is.EqualTo(live.SpawnCount));
            Assert.That(merged.EmitCursor, Is.EqualTo(live.EmitCursor));
            Assert.That(merged.Capacity, Is.EqualTo(live.Capacity));
            Assert.That(merged.SliceOffset, Is.EqualTo(live.SliceOffset));
            Assert.That(merged.DeltaTime, Is.EqualTo(live.DeltaTime));
            Assert.That(merged.EmitterTime, Is.EqualTo(live.EmitterTime));
            Assert.That(merged.FrameSeed, Is.EqualTo(live.FrameSeed));
            Assert.That(merged.WorldMatrix, Is.EqualTo(live.WorldMatrix));
            Assert.That(merged.EmitterHeight, Is.EqualTo(live.EmitterHeight));
            Assert.That(merged.DepthBase, Is.EqualTo(live.DepthBase));
            Assert.That(merged.SpriteTextureId, Is.EqualTo(live.SpriteTextureId));
            Assert.That(merged.ColorGradientTextureId, Is.EqualTo(live.ColorGradientTextureId));
            Assert.That(merged.SizeCurveTextureId, Is.EqualTo(live.SizeCurveTextureId));

            Assert.That(merged.Flags, Is.EqualTo(edited.Flags));
            Assert.That(merged.Life, Is.EqualTo(edited.Life));
            Assert.That(merged.Speed, Is.EqualTo(edited.Speed));
            Assert.That(merged.Size, Is.EqualTo(edited.Size));
            Assert.That(merged.Motion, Is.EqualTo(edited.Motion));
            Assert.That(merged.Tint, Is.EqualTo(edited.Tint));
        });
    }

    /// <summary>Tests that 3D parameter merging preserves internal state and applies editable fields.</summary>
    [Test]
    public void MergeEdited3DPreservesSlotAndBindlessIdsWhileApplyingEdits()
    {
        var live = new EmitterParams3D
        {
            SpawnCount = 8,
            EmitCursor = 16,
            Capacity = 2048,
            SliceOffset = 1024,
            DeltaTime = 0.033f,
            EmitterTime = 7.0f,
            FrameSeed = 0xDEADBEEFu,
            WorldMatrix = Matrix4x4.CreateTranslation(1f, 2f, 3f),
            SpriteTextureId = 11u,
            ColorGradientTextureId = 22u,
            SizeCurveTextureId = 33u,
            Flags = EmitterParams3D.FlagWorldSpace,
            Life = new Vector4(2f, 4f, 0.1f, 0.3f),
        };

        var edited = new EmitterParams3D
        {
            SpawnCount = 0,
            EmitCursor = 0,
            Capacity = 0,
            SliceOffset = 0,
            DeltaTime = 0f,
            EmitterTime = 0f,
            FrameSeed = 0u,
            WorldMatrix = Matrix4x4.Identity,
            SpriteTextureId = 0u,
            ColorGradientTextureId = 0u,
            SizeCurveTextureId = 0u,
            Flags = EmitterParams3D.FlagVelocityStretch | EmitterParams3D.FlagSizeCurve,
            Life = new Vector4(0.25f, 0.75f, 0.05f, 0.5f),
            Speed = new Vector4(3f, 6f, 1f, 0.25f),
            Size = new Vector4(0.5f, 1.5f, 2.0f, 1.2f),
            Motion = new Vector4(0f, 0f, -9.8f, 0.15f),
            Tint = new ColorFloat(0.4f, 0.8f, 1f, 1f),
        };

        EmitterParams3D merged = EmitterParams3D.MergeEdited(in live, in edited);

        Assert.Multiple(() =>
        {
            Assert.That(merged.SpawnCount, Is.EqualTo(live.SpawnCount));
            Assert.That(merged.EmitCursor, Is.EqualTo(live.EmitCursor));
            Assert.That(merged.Capacity, Is.EqualTo(live.Capacity));
            Assert.That(merged.SliceOffset, Is.EqualTo(live.SliceOffset));
            Assert.That(merged.DeltaTime, Is.EqualTo(live.DeltaTime));
            Assert.That(merged.EmitterTime, Is.EqualTo(live.EmitterTime));
            Assert.That(merged.FrameSeed, Is.EqualTo(live.FrameSeed));
            Assert.That(merged.WorldMatrix, Is.EqualTo(live.WorldMatrix));
            Assert.That(merged.SpriteTextureId, Is.EqualTo(live.SpriteTextureId));
            Assert.That(merged.ColorGradientTextureId, Is.EqualTo(live.ColorGradientTextureId));
            Assert.That(merged.SizeCurveTextureId, Is.EqualTo(live.SizeCurveTextureId));

            Assert.That(merged.Flags, Is.EqualTo(edited.Flags));
            Assert.That(merged.Life, Is.EqualTo(edited.Life));
            Assert.That(merged.Speed, Is.EqualTo(edited.Speed));
            Assert.That(merged.Size, Is.EqualTo(edited.Size));
            Assert.That(merged.Motion, Is.EqualTo(edited.Motion));
            Assert.That(merged.Tint, Is.EqualTo(edited.Tint));
        });
    }
}
