#version 450

#pragma topology_primitive line_list
#pragma fill_mode wireframe
#pragma cull_mode none
#pragma blend_state alpha_blend
#pragma depth_test false

layout(set = 0, binding = 0) uniform ModelViewProjectionBuffer
{
    mat4 matrixMVP;
};


#ifdef VERTEX_SHADER

layout(location = 0) in vec3 position;
layout(location = 1) in vec4 color;

layout(location = 0) out vec4 fsin_color;

void main()
{
    gl_Position = matrixMVP * vec4(position, 1.0);
    fsin_color = color;
}

#endif

#ifdef FRAGMENT_SHADER

layout(location = 0) in vec4 fsin_color;
layout(location = 0) out vec4 fsout_color;

void main()
{
    fsout_color = fsin_color;
}

#endif