

vec4 MakeClipSpaceConsistent(vec4 vertex)
{
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

vec2 MakeUVCosistent(vec2 uv)
{
#ifdef BACKEND_OPENGL
    uv.y = 1.0 - uv.y;
#endif

#ifdef BACKEND_OPENGLES
    uv.y = 1.0 - uv.y;
#endif
    return uv;
}