#version 450

layout(set = 0, binding = 0) uniform ViewProjectionBuffer
{
    mat4 viewProj;
};

layout(set = 1, binding = 0) uniform TransformBuffer
{
    mat4 transform;
};

/// Vertex Shader ///
#ifdef VERTEX_SHADER

layout(location = 0) in vec3 position;
layout(location = 1) in vec2 uv;
layout(location = 2) in vec4 color;

layout(location = 0) out vec4 fsin_color;

void main()
{
    vec4 pos = transform * vec4(position, 1.0);
    pos = viewProj * pos;
    gl_Position = pos;
    //gl_Position = vec4(position, 1.0);
    fsin_color = color;
}

#endif

/// Fragment Shader ///
#ifdef FRAGMENT_SHADER

layout(location = 0) in vec4 fsin_color;
layout(location = 0) out vec4 fsout_color;

void main()
{
    fsout_color = fsin_color;
}

#endif
