@group(0) @binding(0) var inputTexture: texture_2d<f32>;
@group(1) @binding(0) var outputTexture: texture_storage_2d<rgba8unorm,write>;

@compute @workgroup_size(8, 8)
fn cs_main(@builtin(global_invocation_id) id: vec3<u32>) {
    //box blur
    var color: vec3<f32> = textureLoad(inputTexture, id.xy, 0).rgb;
    for (var i: i32 = -1; i < 2; i = i + 1) {
        for (var j: i32 = -1; j < 2; j = j + 1) {
            color = color + textureLoad(inputTexture, id.xy + vec2<i32>(i, j), 0).rgb;
        }
    }

    color = color / 9.0;
    textureStore(outputTexture, id.xy, vec4<f32>(color, 1.0));
}