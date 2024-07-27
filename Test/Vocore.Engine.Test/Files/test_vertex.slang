[shader("vertex")]
void test_vertex_main(
    float3 position : ATTRIB_POSITION,
    float2 uv : ATTRIB_TEXCOORD,
    out float4 out_position : SV_POSITION,
    out float2 out_uv : TEXCOORD0
)
{
    out_position = float4(position, 1.0);
    out_uv = uv;
}