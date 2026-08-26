// file "compute-position.slang" - module "sandbox8_compute_position"
//
// Storage buffer access qualifiers derive from their usage.

#language slang 2025
module sandbox8_compute_position;

public struct PositionsParams
{
    public RWStructuredBuffer<float4> positions;
};

public ParameterBlock<PositionsParams> positions;

public struct TimeParams
{
    public float time;
};

public ParameterBlock<TimeParams> time;

[shader("compute")]
[numthreads(8, 1, 1)]
void mainCS(uint3 id: SV_DispatchThreadID) {
    positions.positions[id.x] = float4(id.x, cos(time.time + float(id.x)*0.1)*5, 0.0f, 1.0f);
}
