#version 450

#include "Core.glsl"

#ifdef VERTEX_SHADER

void vertex(){
    float x = _InstanceId%200;
    float y = _InstanceId/200;
    vec3 offset = vec3(x, y, sin(_Time+x+y));
    gl_Position = VertexToClipSpace(_VertexPosition+offset);
}

#endif

#ifdef FRAGMENT_SHADER

void fragment(){
    _OutColor = _PixelColor;
}

#endif