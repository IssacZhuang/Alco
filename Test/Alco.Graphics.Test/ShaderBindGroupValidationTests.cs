using Alco.Graphics;
using Alco.Graphics.NoGPU;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Tests bind group layout validation for contiguous, in-range, and unique group indices.
/// </summary>
[TestFixture]
public class ShaderBindGroupValidationTests
{
    /// <summary>
    /// Builds a <see cref="ShaderReflection"/> whose bind groups use the given set (set) indices,
    /// each holding a single placeholder binding so the reflection info constructs successfully.
    /// </summary>
    private static ShaderReflection CreateReflection(params uint[] groups)
    {
        BindGroupLayout[] layouts = new BindGroupLayout[groups.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            uint group = groups[i];
            layouts[i] = new BindGroupLayout
            {
                Group = group,
                Bindings = new BindGroupEntryInfo[]
                {
                    new() { Entry = new BindGroupEntry(0, ShaderStage.Standard, BindingType.UniformBuffer, name: $"group{group}") }
                }
            };
        }

        return new ShaderReflection(
            Array.Empty<VertexInputLayout>(),
            layouts,
            Array.Empty<PushConstantsRange>(),
            ThreadGroupSize.Default);
    }

    /// <summary>
    /// Tests that a shader with no bind groups is valid.
    /// </summary>
    [Test(Description = "A shader with no bind groups is valid")]
    public void Validate_NoBindGroups_Passes()
    {
        ShaderReflection info = CreateReflection();
        Assert.DoesNotThrow(() => ShaderReflectionUtility.ValidateBindGroupLayouts(info, 8));
    }

    /// <summary>
    /// Tests that contiguous indices starting at zero under the limit are valid.
    /// </summary>
    [Test(Description = "Contiguous indices starting at 0 under the limit are valid")]
    public void Validate_ContiguousUnderLimit_Passes()
    {
        ShaderReflection info = CreateReflection(0, 1, 2);
        Assert.DoesNotThrow(() => ShaderReflectionUtility.ValidateBindGroupLayouts(info, 8));

        ShaderReflection atLimit = CreateReflection(0, 1, 2, 3);
        Assert.DoesNotThrow(() => ShaderReflectionUtility.ValidateBindGroupLayouts(atLimit, 4));
    }

    /// <summary>
    /// Tests that more bind groups than the limit throws.
    /// </summary>
    [Test(Description = "More bind groups than the limit throws")]
    public void Validate_CountExceedsMax_Throws()
    {
        ShaderReflection info = CreateReflection(0, 1, 2, 3, 4);
        ShaderReflectionException ex = Assert.Throws<ShaderReflectionException>(
            () => ShaderReflectionUtility.ValidateBindGroupLayouts(info, 4))!;
        Assert.That(ex.Message, Does.Contain("exceeds the maximum 4"));
    }

    /// <summary>
    /// Tests that a skipped group index throws.
    /// </summary>
    [Test(Description = "A skipped group index throws")]
    public void Validate_SkippedGroupIndex_Throws()
    {
        ShaderReflection info = CreateReflection(0, 2);
        ShaderReflectionException ex = Assert.Throws<ShaderReflectionException>(
            () => ShaderReflectionUtility.ValidateBindGroupLayouts(info, 8))!;
        Assert.That(ex.Message, Does.Contain("contiguous"));
    }

    /// <summary>
    /// Tests that group indices not starting at zero throw.
    /// </summary>
    [Test(Description = "Group indices not starting at 0 throw")]
    public void Validate_NonZeroStart_Throws()
    {
        ShaderReflection info = CreateReflection(1, 2);
        Assert.Throws<ShaderReflectionException>(
            () => ShaderReflectionUtility.ValidateBindGroupLayouts(info, 8));
    }

    /// <summary>
    /// Tests that a duplicate group index throws.
    /// </summary>
    [Test(Description = "A duplicate group index throws")]
    public void Validate_DuplicateGroupIndex_Throws()
    {
        ShaderReflection info = CreateReflection(0, 1, 1);
        Assert.Throws<ShaderReflectionException>(
            () => ShaderReflectionUtility.ValidateBindGroupLayouts(info, 8));
    }

    /// <summary>
    /// Tests that the NoGPU device reports a maximum of eight bind groups.
    /// </summary>
    [Test(Description = "NoDevice reports the representative NoGPU bind-group limit")]
    public void NoDevice_ReportsMaxBindGroupsEight()
    {
        Assert.That(NoDevice.noDevice.MaxBindGroups, Is.EqualTo(8));
    }
}
