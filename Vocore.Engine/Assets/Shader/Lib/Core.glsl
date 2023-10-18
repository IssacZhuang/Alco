layout(set = 0, binding = 0) uniform GlobalBuffer
{
    mat4 _ViewProjMatrix;
    vec2 _ScreenSize;
    float _Time;
    float _DeltaTime;
    float _SinTime;
    float _CosTime;
};

layout(set = 1, binding = 0) uniform TransformBuffer
{
    mat4 _TransformMatrix;
};

vec4 VertexToClipSpace(vec4 vertex)
{
    vec4 pos = _TransformMatrix * vertex;
    return _ViewProjMatrix * pos;
}

vec4 VertexToClipSpace(vec3 vertex)
{
    vec4 pos = _TransformMatrix * vec4(vertex, 1.0);
    return _ViewProjMatrix * pos;
}

struct VertexInput
{
    vec3 position;
    vec2 uv;
    vec4 color;
};

struct FragmentInput
{
    vec2 uv;
    vec4 color;
};

#define PI 3.141592
#define TAU 6.283185
#define E 2.718281

#ifdef VERTEX_SHADER

void vertex();

layout(location = 0) in vec3 _VertexPosition;
layout(location = 1) in vec2 _VertexUV;
layout(location = 2) in vec4 _VertexColor;
#pragma instance_start
layout(location = 3) in int _InstanceId;
#pragma instance_end

layout(location = 0) out vec4 _PixelColor;
layout(location = 1) out vec2 _PixelUV;

void main()
{
    _PixelColor = _VertexColor;
    _PixelUV = _VertexUV;
    vertex();
}

#endif

/// Fragment Shader ///
#ifdef FRAGMENT_SHADER

void fragment();

layout(location = 0) in vec4 _PixelColor;
layout(location = 1) in vec2 _PixelUV;

layout(location = 0) out vec4 _OutColor;

void main()
{
    fragment();
}

#endif