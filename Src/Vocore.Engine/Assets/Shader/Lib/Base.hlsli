float4 MakeClipSpaceConsistent(float4 vertex) {
#ifdef BACKEND_OPENGL
	vertex.z = vertex.z * 2.0 - vertex.w;
#endif

#ifdef BACKEND_OPENGLES
	vertex.z = vertex.z * 2.0 - vertex.w;
#endif

#ifdef BACKEND_VULKAN
	vertex.y = -vertex.y;
#endif
	return vertex;
}

float2 MakeUVCosistent(float2 uv) {
#ifdef BACKEND_OPENGL
	uv.y = 1.0 - uv.y;
#endif

#ifdef BACKEND_OPENGLES
	uv.y = 1.0 - uv.y;
#endif
	return uv;
}

float4 SampleTex2D(Texture2D tex, SamplerState texSampler, float2 uv) {
	uv = MakeUVCosistent(uv);
	return tex.Sample(texSampler, uv);
}

float4 VertexToClipSpace(float4 vertex, float4x4 transformMatrix, float4x4 viewProjMatrix) {
	float4 pos = mul(transformMatrix, vertex);
	return MakeClipSpaceConsistent(mul(viewProjMatrix, pos));
}

float4 VertexToClipSpace(float3 vertex, float4x4 transformMatrix, float4x4 viewProjMatrix) {
	return VertexToClipSpace(float4(vertex, 1.0), transformMatrix, viewProjMatrix);
}