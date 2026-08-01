using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// Helpers for the engine's physically-based procedural sky (see
/// <c>Shaders/Libs/Atmosphere.hlsli</c>): a parametric sun orbit driven by the
/// time of day, plus the sun's atmosphere-transmittance tint evaluated on the
/// CPU so scene lighting matches the sky (white sun at noon, red at sunset,
/// fading out through twilight).
/// <br/>The atmosphere constants MUST stay in sync with Atmosphere.hlsli.
/// </summary>
public static class ProceduralSkyUtility
{
    private const float EarthRadiusKm = 6360.0f;
    private const float AtmosphereRadiusKm = 6420.0f;
    private const float RayleighScaleHeightKm = 8.0f;
    private const float MieScaleHeightKm = 1.2f;
    private const float ViewHeightKm = 0.2f;
    private static readonly Vector3 BetaRayleigh = new(5.5e-3f, 13.0e-3f, 22.4e-3f); // 1/km
    private const float BetaMie = 21.0e-3f; // 1/km (extinction is 1.1x)
    // Ozone absorption (Chappuis bands), sharing the rayleigh density integral.
    private static readonly Vector3 BetaOzone = new(0.650e-3f, 1.881e-3f, 0.085e-3f); // 1/km

    /// <summary>
    /// Normalized direction from the viewer toward the sun for a time of day.
    /// The sun rises at <paramref name="eastAzimuthRad"/>, culminates at
    /// <paramref name="maxElevationDeg"/> at noon (hour 12) and sets at the
    /// opposite azimuth at hour 18; below the horizon at night.
    /// </summary>
    /// <param name="hourOfDay">Time of day in hours; values outside [0, 24) wrap.</param>
    /// <param name="maxElevationDeg">Sun elevation at noon in degrees.</param>
    /// <param name="eastAzimuthRad">Azimuth in radians (around +Z from +X) where the sun rises.</param>
    /// <returns>Normalized direction toward the sun; +Z is up.</returns>
    public static Vector3 GetDirectionToSun(float hourOfDay, float maxElevationDeg = 60.0f, float eastAzimuthRad = 0.0f)
    {
        float hourAngle = (hourOfDay - 12.0f) / 24.0f * MathF.Tau; // 0 at noon
        float elevation = MathF.Cos(hourAngle) * maxElevationDeg * MathF.PI / 180.0f;
        float azimuth = eastAzimuthRad + hourAngle + MathF.PI / 2.0f;
        float cosElevation = MathF.Cos(elevation);
        return new Vector3(
            cosElevation * MathF.Cos(azimuth),
            cosElevation * MathF.Sin(azimuth),
            MathF.Sin(elevation));
    }

    /// <summary>
    /// Sun light color after atmosphere extinction along the sun direction:
    /// white when the sun is high, increasingly red toward the horizon, black
    /// once the earth occludes the sun.
    /// </summary>
    /// <param name="directionToSun">Normalized direction toward the sun.</param>
    /// <returns>Linear RGB transmittance in [0, 1].</returns>
    public static Vector3 GetSunColor(Vector3 directionToSun)
    {
        Vector3 origin = new(0.0f, 0.0f, EarthRadiusKm + ViewHeightKm);
        if (RayHitsSphere(origin, directionToSun, EarthRadiusKm))
        {
            return Vector3.Zero;
        }

        // March to the top of the atmosphere accumulating the density integral.
        const int sampleCount = 12;
        float tEnd = RayExitDistance(origin, directionToSun, AtmosphereRadiusKm);
        float dt = tEnd / sampleCount;
        float rayleighDensity = 0.0f;
        float mieDensity = 0.0f;
        for (int i = 0; i < sampleCount; i++)
        {
            Vector3 p = origin + directionToSun * ((i + 0.5f) * dt);
            float height = p.Length() - EarthRadiusKm;
            rayleighDensity += MathF.Exp(-height / RayleighScaleHeightKm) * dt;
            mieDensity += MathF.Exp(-height / MieScaleHeightKm) * dt;
        }

        Vector3 tau = BetaRayleigh * rayleighDensity
            + new Vector3(BetaMie * 1.1f * mieDensity)
            + BetaOzone * rayleighDensity;
        return new Vector3(MathF.Exp(-tau.X), MathF.Exp(-tau.Y), MathF.Exp(-tau.Z));
    }

    /// <summary>
    /// Direct-light scale for the sun: 1 in daylight, fading smoothly to 0
    /// across twilight (sun elevation roughly +0.3° to -2.3°) so shadowed
    /// lighting does not linger once the disc has set.
    /// </summary>
    /// <param name="directionToSun">Normalized direction toward the sun.</param>
    public static float GetSunLightScale(Vector3 directionToSun)
    {
        float t = Math.Clamp((directionToSun.Z + 0.04f) / 0.045f, 0.0f, 1.0f);
        return EasingUtility.SmoothStep(t);
    }

    private static bool RayHitsSphere(Vector3 origin, Vector3 dir, float radius)
    {
        float b = Vector3.Dot(origin, dir);
        float c = origin.LengthSquared() - radius * radius;
        float discriminant = b * b - c;
        return discriminant >= 0.0f && -b - MathF.Sqrt(discriminant) > 0.0f;
    }

    private static float RayExitDistance(Vector3 origin, Vector3 dir, float radius)
    {
        float b = Vector3.Dot(origin, dir);
        float c = origin.LengthSquared() - radius * radius;
        return -b + MathF.Sqrt(b * b - c);
    }
}
