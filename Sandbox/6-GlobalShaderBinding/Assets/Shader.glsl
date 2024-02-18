#version 450

#extension GL_EXT_samplerless_texture_functions : enable

#define STRUCT_UNIFORM(slot, name) layout(set = slot, binding = 0) uniform name
#define STRUCT_STORAGE(slot, name) layout(set = slot, binding = 0) buffer name
#define TEXTURE_SAMPLE(slot, name) layout(set = slot, binding = 0) uniform texture2D name; layout(set = slot, binding = 1) uniform sampler name##Sampler

#define INPUT(slot, type, name) layout(location = slot) in type name
#define OUTPUT(slot, type, name) layout(location = slot) out type name

#define TEXTURE_READ(slot, name) layout(set = slot, binding = 0) uniform texture2D name
#define TEXTURE_STORAGE(slot, format, name) layout(set = slot, binding = 0, format) uniform writeonly image2D name
#define COMPUTE_THREADS(x, y, z) layout(local_size_x = x, local_size_y = y, local_size_z = z) in

// offset 16 will not affect the causual uniform buffer
layout(set = 0, binding = 16) uniform GlobalData{
    vec2 screenSize;
    vec2 screenSizeInv;
    float time;
    float deltaTime;
    float sinTime;
    float cosTime;
    mat4 camera3D;
    mat3 camera2D;
};