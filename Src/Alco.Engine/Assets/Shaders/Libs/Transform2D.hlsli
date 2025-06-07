struct Transform2D{
    float2 position;
    float2 rotation;
    float2 scale;
};

// Rotates a 2D vector using the rotation component of Rotation2D (left-handed rotation)
// position: The vector to rotate
// rotation: The rotation to apply, the x component is the cosine and the y component is the sine
// Returns: The rotated vector
float2 rotate(float2 position, float2 rotation)
{
    float c = rotation.x; // cosine component
    float s = rotation.y; // sine component
    // Create 2x2 rotation matrix and multiply - more efficient on GPU
    float2x2 rotMatrix = float2x2(c, s, -s, c);
    return mul(rotMatrix, position);
}

