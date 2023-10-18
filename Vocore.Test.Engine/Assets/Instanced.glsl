#version 450

#include "Core.glsl"
#include "Noise.glsl"

#ifdef VERTEX_SHADER

layout(location = 2) out vec4 _WorldPosition;

void vertex(){
    float x = _InstanceId%200;
    float y = _InstanceId/200;
    vec3 offset = vec3(x, y, 0);
    vec4 pos = vec4((_VertexPosition + offset), 1);
    pos = _TransformMatrix * pos;
    _WorldPosition = pos;
    pos = _ViewProjMatrix * pos;
    gl_Position = pos;
    
}

#endif

#ifdef FRAGMENT_SHADER

layout(location = 2) in vec4 _WorldPosition;

void fragment(){
    fnl_state state = fnlCreateState(1337);
    state.noise_type = FNL_NOISE_CELLULAR;
    state.fractal_type = FNL_FRACTAL_FBM;
	state.frequency = .01f;
	state.octaves = 4;
	state.lacunarity = 2.f;
	state.gain = .5f;
    
    float noise = fnlGetNoise3D(state, _WorldPosition.x, _Time * 20.f, _WorldPosition.y) / 2.f + 0.5f;
    noise = noise * 2;
    _OutColor = vec4(0,noise,noise,1.0);
}

#endif