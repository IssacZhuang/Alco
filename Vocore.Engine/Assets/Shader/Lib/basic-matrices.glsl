layout(set = 0, binding = 0) uniform GlobalBuffer
{
    mat4 viewProj;
};

layout(set = 1, binding = 0) uniform TransformBuffer
{
    mat4 transform;
};

vec4 VertexToClipSpace(vec4 vertex)
{
    vec4 pos = transform * vertex;
    return viewProj * pos;
}

vec4 VertexToClipSpace(vec3 vertex)
{
    vec4 pos = transform * vec4(vertex, 1.0);
    return viewProj * pos;
}