using System.Numerics;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// JSON schema of a World3D material asset file (<c>.amat</c> with <c>"type": "pbr"</c>):
/// the pipeline-agnostic base schema plus the built-in PbrStandard surface's flat factor
/// and alpha/double-sided routing fields. Maps onto <see cref="PbrMaterialAsset"/>.
/// </summary>
public sealed class PbrMaterialAssetJson : MaterialAssetJson
{
    public float[]? BaseColorFactor { get; set; }
    public float? MetallicFactor { get; set; }
    public float? RoughnessFactor { get; set; }
    public float[]? EmissiveFactor { get; set; }
    public string? AlphaMode { get; set; }
    public float? AlphaCutoff { get; set; }
    public bool? DoubleSided { get; set; }

    /// <inheritdoc />
    protected override MaterialAsset Map(string filename)
    {
        Validate(filename);
        return new PbrMaterialAsset
        {
            Name = MapName(filename),
            SurfaceShader = AssetJson.NormalizePath(Shader),
            Defines = MapDefines(Defines, filename),
            Textures = MapTextures(Textures),
            Parameters = MapParameters(Parameters, filename),
            BaseColorFactor = BaseColorFactor != null ? ToVector4(BaseColorFactor, "baseColorFactor", filename) : Vector4.One,
            MetallicFactor = MetallicFactor ?? 0.0f,
            RoughnessFactor = RoughnessFactor ?? 1.0f,
            EmissiveFactor = EmissiveFactor != null ? ToVector3(EmissiveFactor, "emissiveFactor", filename) : Vector3.Zero,
            AlphaMode = ParseAlphaMode(AlphaMode, filename),
            AlphaCutoff = AlphaCutoff ?? 0.5f,
            DoubleSided = DoubleSided ?? false,
        };
    }

    private static MeshAlphaMode ParseAlphaMode(string? alphaMode, string filename)
    {
        if (string.IsNullOrWhiteSpace(alphaMode))
        {
            return MeshAlphaMode.Opaque;
        }

        if (Enum.TryParse<MeshAlphaMode>(alphaMode, ignoreCase: true, out MeshAlphaMode parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new InvalidDataException(
            $"Material asset '{filename}' has unknown alphaMode '{alphaMode}'; expected one of: {string.Join(", ", Enum.GetNames<MeshAlphaMode>())}.");
    }

    private static Vector4 ToVector4(float[] values, string field, string filename)
    {
        if (values.Length != 4)
        {
            throw new InvalidDataException($"Material asset '{filename}' field '{field}' must have 4 components, got {values.Length}.");
        }
        return new Vector4(values[0], values[1], values[2], values[3]);
    }

    private static Vector3 ToVector3(float[] values, string field, string filename)
    {
        if (values.Length != 3)
        {
            throw new InvalidDataException($"Material asset '{filename}' field '{field}' must have 3 components, got {values.Length}.");
        }
        return new Vector3(values[0], values[1], values[2]);
    }
}
