#version 450

layout(set = 0, binding = 0) uniform Ubo {
    vec3 color;
} ubo;

layout(set = 1, binding = 0) uniform texture2D image;
layout(set = 1, binding = 1) uniform sampler imageSampler;

#ifdef VERTEX

layout(location = 0) in vec3 position;
layout(location = 1) in vec3 inColor;
layout(location = 2) in vec2 texCoord;

layout(location = 0) out vec2 fragTexCoord;

void main() {
    gl_Position = vec4(position, 1.0);
    fragTexCoord = texCoord;
}

#endif

#ifdef FRAGMENT


layout(location = 0) in vec2 fragTexCoord;

layout(location = 0) out vec4 outColor;

void main() {
    vec4 result = texture(sampler2D(image, imageSampler), fragTexCoord) * vec4(ubo.color, 1.0);
    //inverse gamma correction
    result = pow(result, vec4(2.2));
    outColor = result;
}

#endif
