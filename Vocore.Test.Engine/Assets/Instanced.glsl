#version 450

#include "Core.glsl"

#ifdef VERTEX_SHADER

void vertex(){
    vec3 offset = vec3(_InstanceId%200, _InstanceId/200, 0);
    gl_Position = VertexToClipSpace(_VertexPosition+offset);
}

#endif

#ifdef FRAGMENT_SHADER

void fragment(){
    _OutColor = _PixelColor;
}

#endif