#ifndef ALCO_PBR_GEOMETRY_NORMAL_HLSLI
#define ALCO_PBR_GEOMETRY_NORMAL_HLSLI

// Octahedral encoding keeps the interpolated mesh normal in the two half-float
// alpha channels of the normal and emissive G-buffer targets. Diffuse GI needs
// this stable low-frequency normal; the regular xyz normal remains the
// normal-mapped value used by direct and specular lighting.
float2 EncodeGeometryNormal(float3 normal)
{
    normal /= max(abs(normal.x) + abs(normal.y) + abs(normal.z), 0.0001);
    float2 encoded = normal.xy;
    if (normal.z < 0.0)
    {
        encoded = (1.0 - abs(encoded.yx))
            * float2(encoded.x >= 0.0 ? 1.0 : -1.0,
                     encoded.y >= 0.0 ? 1.0 : -1.0);
    }
    return encoded * 0.5 + 0.5;
}

float3 DecodeGeometryNormal(float2 encoded)
{
    float2 octahedron = encoded * 2.0 - 1.0;
    float3 normal = float3(
        octahedron,
        1.0 - abs(octahedron.x) - abs(octahedron.y));
    if (normal.z < 0.0)
    {
        normal.xy = (1.0 - abs(normal.yx))
            * float2(normal.x >= 0.0 ? 1.0 : -1.0,
                     normal.y >= 0.0 ? 1.0 : -1.0);
    }
    return normalize(normal);
}

#endif
