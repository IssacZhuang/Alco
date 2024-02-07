// Vertex shader

@group(0) @binding(0) 
var<uniform> color: vec3<f32>;

@group(1) @binding(0) 
var image: texture_2d<f32>;
@group(1) @binding(1) 
var imageSampler: sampler;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) color: vec3<f32>,
    @location(2) texCoord: vec2<f32>,
};

struct VertexOutput {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) color: vec3<f32>,
    @location(1) texCoord: vec2<f32>,
};

@vertex
fn vs_main(
    model: VertexInput,
) -> VertexOutput {
    var out: VertexOutput;
    out.color = model.color;
    out.clip_position = vec4<f32>(model.position, 1);
    out.texCoord = model.texCoord;
    return out;
}

// Fragment shader

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    var color:vec4<f32> = vec4<f32>(color, 1.0) * textureSample(image, imageSampler, in.texCoord);
    //inverse gamma correction, why??
    color = vec4<f32>(pow(color, vec4<f32>(2.2)));
    return color;
}