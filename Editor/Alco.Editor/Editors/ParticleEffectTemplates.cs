using Alco.IO;
using Alco.Particles;

namespace Alco.Editor;

/// <summary>
/// Minimal authoring templates for new particle effect assets (<c>.afx</c>) and the
/// unique-name helper the asset browser's "New" menu uses. The templates reference no
/// textures or materials, so they load and render in any project (the default particle
/// surface shades plain colored quads).
/// </summary>
public static class ParticleEffectTemplates
{
    /// <summary>A minimal looping 2D effect: one circle emitter fading out over life.</summary>
    public const string Effect2D = """
        {
            "$type": "Alco.Particles.ParticleEffect2DAsset",
            "version": "1.0",
            "groups": [
                {
                    "name": "Group",
                    "maxParticles": 1024,
                    "looping": true,
                    "emissionRate": 60,
                    "shape": { "type": "circle", "radius": 1.0 },
                    "direction": { "x": 0, "y": 1 },
                    "spreadAngle": 0.35,
                    "speed": { "min": 6, "max": 12 },
                    "lifetime": { "min": 0.8, "max": 1.6 },
                    "size": { "min": { "x": 1.0, "y": 1.0 }, "max": { "x": 2.0, "y": 2.0 } },
                    "startColor": { "min": "#FFFFFFFF", "max": "#FFFFFFFF" },
                    "endColor": "#FFFFFF00",
                    "fadeIn": 0.1,
                    "fadeOut": 0.5,
                    "blend": "AlphaBlend"
                }
            ]
        }
        """;

    /// <summary>A minimal looping 3D effect: one sphere emitter fading out over life.</summary>
    public const string Effect3D = """
        {
            "$type": "Alco.Particles.ParticleEffect3DAsset",
            "version": "1.0",
            "groups": [
                {
                    "name": "Group",
                    "maxParticles": 1024,
                    "looping": true,
                    "emissionRate": 60,
                    "shape": { "type": "sphere", "radius": 0.5 },
                    "direction": { "x": 0, "y": 0, "z": 1 },
                    "spreadAngle": 0.35,
                    "speed": { "min": 2, "max": 4 },
                    "lifetime": { "min": 0.8, "max": 1.6 },
                    "size": { "min": 0.4, "max": 0.8 },
                    "startColor": { "min": "#FFFFFFFF", "max": "#FFFFFFFF" },
                    "endColor": "#FFFFFF00",
                    "fadeIn": 0.1,
                    "fadeOut": 0.5,
                    "blend": "AlphaBlend"
                }
            ]
        }
        """;

    /// <summary>
    /// Finds a free asset-system-relative path for a new asset, appending
    /// <c>_2</c>, <c>_3</c>, … to <paramref name="baseName"/> until the name is
    /// unused on disk and in the asset system.
    /// </summary>
    /// <param name="context">The editor context (project and asset system).</param>
    /// <param name="directory">
    /// The asset-system-relative directory to create the asset in ("" for an owned root).
    /// </param>
    /// <param name="baseName">The file base name without extension (e.g. <c>NewEffect2D</c>).</param>
    /// <returns>The free relative path including the particle effect extension.</returns>
    public static string GetUniqueAssetPath(EditorContext context, string directory, string baseName)
    {
        string prefix = directory.Length > 0 ? directory.TrimEnd('/') + "/" : string.Empty;
        for (int i = 1; ; i++)
        {
            string candidate = $"{prefix}{baseName}{(i == 1 ? string.Empty : "_" + i)}{ParticleAssetPipeline.EffectExtension}";
            if (context.AssetSystem.IsFileExist(candidate) || context.Project.IsOwnedAsset(candidate))
            {
                continue;
            }
            return candidate;
        }
    }
}
