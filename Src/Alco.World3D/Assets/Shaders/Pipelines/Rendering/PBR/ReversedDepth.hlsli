#ifndef PBR_REVERSED_DEPTH_HLSLI
#define PBR_REVERSED_DEPTH_HLSLI

// The PBR pipeline's camera depth is reversed and infinite-far: NDC z maps the
// near plane to 1.0 and the far plane (at infinity) to 0.0 (see
// CameraDataPerspective.ReverseInfiniteDepth on the C# side). Sky pixels keep
// exactly the 0.0 depth the G-buffer pass cleared to. The shadow atlas and RSM
// stay on the forward convention (0 = near, 1 = far); their compares and
// biases are untouched.

// Depths at or below this are sky: 1e-6 corresponds to z_view = near / 1e-6
// (~100 km at near = 0.1), beyond any scene geometry, and only the cleared
// sky value of 0.0 falls below it.
#define PBR_SKY_DEPTH_EPSILON 1e-6
#define IS_SKY_DEPTH(d) ((d) <= PBR_SKY_DEPTH_EPSILON)

#endif // PBR_REVERSED_DEPTH_HLSLI
