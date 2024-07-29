module lib;


public float grayscale(float3 color)
{
    return dot(color, float3(0.299, 0.587, 0.114));
}

