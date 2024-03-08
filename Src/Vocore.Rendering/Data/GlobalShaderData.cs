using System;
using System.Numerics;
using Vocore.Unsafe;

namespace Vocore.Rendering;


public struct GlobalShaderData
{
    public Vector2 screenSize;
    public Vector2 screenSizeInv;
    public Matrix4x4 camera;
    public float time;
    public float deltaTime;
    public float sinTime;
    public float cosTime;
}


