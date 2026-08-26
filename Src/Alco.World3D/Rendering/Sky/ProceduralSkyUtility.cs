using System.Numerics;

using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Helpers for the engine's physically-based procedural sky (see
/// <c>Shaders/Libs/AlcoWorld3D_Atmosphere.slang</c>): a parametric sun orbit driven by the
/// time of day, plus the sun's atmosphere-transmittance tint evaluated on the
/// CPU so scene lighting matches the sky (white sun at noon, red at sunset,
/// fading out through twilight).
/// <br/>The atmosphere constants MUST stay in sync with Atmosphere.slang.
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
    private const int SkyViewSampleCount = 8;
    private const int SkySunSampleCount = 4;
    private const int HorizonDirectionCount = 8;
    private const float HorizonElevation = 0.15f;
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
        if (!TryGetAtmosphereDensityIntegral(origin, directionToSun, 12, out Vector2 density))
        {
            return Vector3.Zero;
        }

        Vector3 tau = BetaRayleigh * density.X
            + new Vector3(BetaMie * 1.1f * density.Y)
            + BetaOzone * density.X;
        return new Vector3(MathF.Exp(-tau.X), MathF.Exp(-tau.Y), MathF.Exp(-tau.Z));
    }

    /// <summary>
    /// Evaluates an azimuthally filtered, low-frequency representation of the
    /// current physical sky for sparse environment-lighting techniques such as
    /// voxel cone tracing. The visible sky remains unfiltered.
    /// </summary>
    /// <param name="directionToSun">Normalized direction toward the sun.</param>
    /// <param name="rayleighScale">Rayleigh density multiplier.</param>
    /// <param name="mieScale">Mie density multiplier.</param>
    /// <param name="exposure">Linear sky exposure multiplier.</param>
    /// <param name="nightFloor">Minimum night-sky radiance.</param>
    /// <param name="sunRadianceScale">Solar radiance driving atmospheric scattering.</param>
    /// <param name="horizonColor">Receives the azimuthally averaged near-horizon radiance.</param>
    /// <param name="zenithColor">Receives the zenith radiance.</param>
    public static void GetSkyRadianceGradient(
        Vector3 directionToSun,
        float rayleighScale,
        float mieScale,
        float exposure,
        float nightFloor,
        float sunRadianceScale,
        out Vector3 horizonColor,
        out Vector3 zenithColor)
    {
        float horizontalScale = MathF.Sqrt(1.0f - HorizonElevation * HorizonElevation);
        horizonColor = Vector3.Zero;
        for (int i = 0; i < HorizonDirectionCount; i++)
        {
            float azimuth = i * MathF.Tau / HorizonDirectionCount;
            Vector3 direction = new(
                MathF.Cos(azimuth) * horizontalScale,
                MathF.Sin(azimuth) * horizontalScale,
                HorizonElevation);
            horizonColor += GetSkyRadiance(
                direction,
                directionToSun,
                rayleighScale,
                mieScale,
                exposure,
                nightFloor,
                sunRadianceScale);
        }
        horizonColor /= HorizonDirectionCount;
        zenithColor = GetSkyRadiance(
            Vector3.UnitZ,
            directionToSun,
            rayleighScale,
            mieScale,
            exposure,
            nightFloor,
            sunRadianceScale);
    }

    /// <summary>
    /// Direct-light scale for the sun: 1 in daylight, fading smoothly to 0
    /// across twilight (sun elevation roughly +0.3° to -2.3°) so shadowed
    /// lighting does not linger once the disc has set.
    /// </summary>
    /// <param name="directionToSun">Normalized direction toward the sun.</param>
    /// <returns>The direct-sun intensity multiplier.</returns>
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

    private static Vector3 GetSkyRadiance(
        Vector3 rayDirection,
        Vector3 directionToSun,
        float rayleighScale,
        float mieScale,
        float exposure,
        float nightFloor,
        float sunRadianceScale)
    {
        Vector3 origin = new(0.0f, 0.0f, EarthRadiusKm + ViewHeightKm);
        float rayLength = RayExitDistance(origin, rayDirection, AtmosphereRadiusKm);
        float stepLength = rayLength / SkyViewSampleCount;
        Vector3 betaRayleigh = BetaRayleigh * rayleighScale;
        float betaMie = BetaMie * mieScale;
        float cosTheta = Vector3.Dot(rayDirection, directionToSun);
        float rayleighPhase = 3.0f / (16.0f * MathF.PI) * (1.0f + cosTheta * cosTheta);

        // The direct sun is handled separately. An isotropic Mie phase removes
        // its narrow forward peak before this sky is sampled by sparse cones.
        float miePhase = 1.0f / (4.0f * MathF.PI);
        Vector3 rayleighSum = Vector3.Zero;
        Vector3 mieSum = Vector3.Zero;
        Vector2 viewDensity = Vector2.Zero;
        for (int i = 0; i < SkyViewSampleCount; i++)
        {
            Vector3 position = origin + rayDirection * ((i + 0.5f) * stepLength);
            float height = position.Length() - EarthRadiusKm;
            Vector2 localDensity = new(
                MathF.Exp(-height / RayleighScaleHeightKm) * stepLength,
                MathF.Exp(-height / MieScaleHeightKm) * stepLength);
            viewDensity += localDensity;

            if (!TryGetAtmosphereDensityIntegral(
                position,
                directionToSun,
                SkySunSampleCount,
                out Vector2 sunDensity))
            {
                continue;
            }

            Vector3 opticalDepth = betaRayleigh * (viewDensity.X + sunDensity.X)
                + new Vector3(betaMie * 1.1f * (viewDensity.Y + sunDensity.Y))
                + BetaOzone * (viewDensity.X + sunDensity.X);
            Vector3 transmittance = Exp(-opticalDepth);
            rayleighSum += localDensity.X * transmittance;
            mieSum += localDensity.Y * transmittance;
        }

        Vector3 radiance = sunRadianceScale
            * (rayleighSum * betaRayleigh * rayleighPhase + mieSum * betaMie * miePhase);
        float daylight = Math.Clamp((directionToSun.Z + 0.08f) / 0.14f, 0.0f, 1.0f);
        float night = 1.0f - EasingUtility.SmoothStep(daylight);
        radiance += nightFloor * night * new Vector3(0.5f, 0.7f, 1.0f);
        return radiance * exposure;
    }

    private static bool TryGetAtmosphereDensityIntegral(
        Vector3 origin,
        Vector3 direction,
        int sampleCount,
        out Vector2 density)
    {
        density = Vector2.Zero;
        if (RayHitsSphere(origin, direction, EarthRadiusKm))
        {
            return false;
        }

        float rayLength = RayExitDistance(origin, direction, AtmosphereRadiusKm);
        float stepLength = rayLength / sampleCount;
        for (int i = 0; i < sampleCount; i++)
        {
            Vector3 position = origin + direction * ((i + 0.5f) * stepLength);
            float height = position.Length() - EarthRadiusKm;
            density += new Vector2(
                MathF.Exp(-height / RayleighScaleHeightKm),
                MathF.Exp(-height / MieScaleHeightKm)) * stepLength;
        }
        return true;
    }

    private static Vector3 Exp(Vector3 value)
    {
        return new Vector3(MathF.Exp(value.X), MathF.Exp(value.Y), MathF.Exp(value.Z));
    }
}
