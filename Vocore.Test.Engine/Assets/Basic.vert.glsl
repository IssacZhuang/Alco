#version 450

#pragma vertext main

layout(set = 0, binding = 0) uniform ViewProjectionBuffer
{
    mat4 viewProj;
};

layout(set = 1, binding = 0) uniform TransformBuffer
{
    mat4 transform;
};

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