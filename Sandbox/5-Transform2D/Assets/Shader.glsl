#version 450

#define STRUCT_UNIFORM(slot, name) layout(set = slot, binding = 0) uniform name
#define STRUCT_STORAGE(slot, name) layout(set = slot, binding = 0) buffer name
#define TEXTURE_SAMPLE(slot, name) layout(set = slot, binding = 0) uniform texture2D name; layout(set = slot, binding = 1) uniform sampler name##Sampler

#define INPUT(slot, type, name) layout(location = slot) in type name
#define OUTPUT(slot, type, name) layout(location = slot) out type name

STRUCT_UNIFORM(0, Camera){
    mat3x3 viewProj;
};

STRUCT_UNIFORM(1, Obj){
    mat3x3 model;
};

TEXTURE_SAMPLE(2, image);

#ifdef VERTEX

INPUT(0, vec2, position);
INPUT(1, vec2, uv);

OUTPUT(0, vec2, fragUv);

void main(){
    vec3 pos = viewProj*model*vec3(position,1);
    gl_Position = vec4(pos.xy,0,1);
    fragUv = uv;
}

#endif

#ifdef FRAGMENT

INPUT(0, vec2, fragUv);

OUTPUT(0, vec4, outColor);

void main(){
    outColor = texture(sampler2D(image, imageSampler), fragUv);
}

#endif

