#define INSTANCE_SHADER

#pragma max_instance_count 5000
#define INTANCE_COUNT 5000

layout(set = 2, binding = 0) uniform InstacingTransformBuffer
{
    mat4 _TransformMatrixArray[INTANCE_COUNT];
};