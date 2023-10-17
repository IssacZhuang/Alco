#version 450


#include "basic-matrices.glsl"

/// Vertex Shader ///
#ifdef VERTEX_SHADER

layout(location = 0) in vec3 position;
layout(location = 1) in vec2 uv;
layout(location = 2) in vec4 color;

layout(location = 0) out vec4 fsin_color;



void main()
{
    gl_Position = VertexToClipSpace(position);
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
