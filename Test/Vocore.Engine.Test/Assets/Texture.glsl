#version 450

#include "Core.glsl"

#ifdef VERTEX_SHADER

void vertex(){
    gl_Position = VertexToClipSpace(_VertexPosition);
}

#endif

#ifdef FRAGMENT_SHADER

layout(set = 2, binding = 0) uniform texture2D Texture;
layout(set = 2, binding = 1) uniform sampler Sampler;

void fragment(){
    _OutColor = Tex2D(Texture, Sampler, _PixelUV);
}

#endif