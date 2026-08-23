// file "alco-rendering-transform2d.slang" - module "alco-rendering-transform2d"
//
// Slang port of Shaders/Libs/Transform2D.hlsli: the 2D transform value type
// shared by the CPU particle systems and their shaders.

#language slang 2025
module alco_rendering_transform2d;

public struct Transform2D
{
    public float2 position;
    public float2 rotation;
    public float2 scale;
};

// Rotates a 2D vector using the rotation component of Rotation2D (left-handed rotation)
// position: The vector to rotate
// rotation: The rotation to apply, the x component is the cosine and the y component is the sine
// Returns: The rotated vector
public float2 rotate(float2 position, float2 rotation)
{
    float s = rotation.x; // sine component
    float c = rotation.y; // cosine component
    // Create 2x2 rotation matrix and multiply - more efficient on GPU
    float2x2 rotMatrix = float2x2(c, s, -s, c);
    return mul(rotMatrix, position);
}
