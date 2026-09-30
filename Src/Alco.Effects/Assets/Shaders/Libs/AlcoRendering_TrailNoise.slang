// file "AlcoRendering_TrailNoise.slang" - module "AlcoRendering_TrailNoise"
//
// Shared smoke density noise. Integer lattice hashes give a shared corner
// exactly the same value from either neighboring cell. A sin(dot(...)) hash
// amplifies floating-point reassociation into visible cell-boundary seams.

#language slang 2025
module AlcoRendering_TrailNoise;

float hashLattice(float2 p)
{
    // Signed coordinates preserve the noise field when its drift crosses zero.
    uint2 cell = asuint(int2(p));
    uint value = cell.x * 0x9E3779B9u ^ cell.y * 0x85EBCA6Bu;
    value ^= value >> 16;
    value *= 0x7FEB352Du;
    value ^= value >> 15;
    value *= 0x846CA68Bu;
    value ^= value >> 16;
    return float(value >> 8) * (1.0 / 16777216.0);
}

float valueNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(hashLattice(i), hashLattice(i + float2(1, 0)), f.x),
                lerp(hashLattice(i + float2(0, 1)), hashLattice(i + float2(1, 1)), f.x), f.y);
}

// The smoke density field retains its original octave scales and weights.
public float TrailFbm2D(float2 p)
{
    return valueNoise(p) * 0.6 + valueNoise(p * 2.3) * 0.3 + valueNoise(p * 5.1) * 0.1;
}
