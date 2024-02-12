#version 450

#extension GL_EXT_samplerless_texture_functions : enable

#define STRCUT(slot, name) layout(set = slot, binding = 0) uniform name
#define SAMPLED_TEXTURE(slot, name) layout(set = slot, binding = 0) uniform texture2D name; layout(set = slot, binding = 1) uniform sampler name##Sampler

#define READ_TEXTURE(slot, name) layout(set = slot, binding = 0) uniform texture2D name
#define STORAGE_TEXTURE(slot, format, name) layout(set = slot, binding = 0, format) uniform writeonly image2D name
#define INPUT(slot, type, name) layout(location = slot) in type name
#define OUTPUT(slot, type, name) layout(location = slot) out type name
#define COMPUTE_THREADS(x, y, z) layout(local_size_x = x, local_size_y = y, local_size_z = z) in


READ_TEXTURE(0, inputTexture);
STORAGE_TEXTURE(1, rgba8, outputTexture);

COMPUTE_THREADS(8, 8, 1);

void main() {
    ivec2 id = ivec2(gl_GlobalInvocationID.xy);
    vec4 color = texelFetch(inputTexture, id, 0);
    int size = 16;
    // for (int i = -1; i < 2; i++) {
    //     for (int j = -1; j < 2; j++) {
    //         ivec2 pos = id + ivec2(i, j);
    //         color += texelFetch(inputTexture, pos, 0);
    //     }
    // }
    for (int i = -size; i < size; i++) {
        for (int j = -size; j < size; j++) {
            ivec2 pos = id + ivec2(i, j);
            color += texelFetch(inputTexture, pos, 0);
        }
    }
    // color /= 9.0;
    color /= (2 * size + 1) * (2 * size + 1);
    imageStore(outputTexture, id, color);
}
