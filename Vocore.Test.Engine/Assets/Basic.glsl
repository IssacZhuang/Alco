#version 450

#include "Core.glsl"

#ifdef VERTEX_SHADER

void vertex(){
    gl_Position = VertexToClipSpace(_VertexPosition);
}

#endif

#ifdef FRAGMENT_SHADER

void fragment(){
    _OutColor = _PixelColor;
}

#endif