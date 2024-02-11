#version 450

#define STRCUT(slot, name) layout(set = slot, binding = 0) uniform name
#define TEXTURE(slot, name) layout(set = slot, binding = 0) uniform texture2D name; layout(set = slot, binding = 1) uniform sampler name##Sampler
#define STORAGE_TEXTURE(slot, format, name) layout(set = slot, binding = 0, format) uniform writeonly image2D name
#define INPUT(slot, type, name) layout(location = slot) in type name
#define OUTPUT(slot, type, name) layout(location = slot) out type name

// layout(set = 0, binding = 0) uniform Ubo {
//     vec3 color;
// };

// layout(set = 1, binding = 0) uniform texture2D image;
// layout(set = 1, binding = 1) uniform sampler imageSampler;

STRCUT(0, Ubo) {
    vec3 color;
};

TEXTURE(1, image);

#ifdef VERTEX

// layout(location = 0) in vec3 position;
// layout(location = 1) in vec3 inColor;
// layout(location = 2) in vec2 texCoord;

INPUT(0, vec3, position);
INPUT(1, vec3, inColor);
INPUT(2, vec2, texCoord);

// layout(location = 0) out vec2 fragTexCoord;
OUTPUT(0, vec2, fragTexCoord);

void main() {
    gl_Position = vec4(position, 1.0);
    fragTexCoord = texCoord;
}

#endif

#ifdef FRAGMENT

// layout(location = 0) in vec2 fragTexCoord;
INPUT(0, vec2, fragTexCoord);

// layout(location = 0) out vec4 outColor;
OUTPUT(0, vec4, outColor);

void main() {
    vec4 result = texture(sampler2D(image, imageSampler), fragTexCoord) * vec4(color, 1.0);
    //inverse gamma correction
    result = pow(result, vec4(2.2));
    outColor = result;
}

#endif
