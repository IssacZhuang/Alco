using System.Numerics;

namespace Alco.Rendering;

public static class MeshUtility
{

    /// <summary>
    /// Generate 9-slice mesh data for UI elements that need to scale without distorting corners and edges.
    /// </summary>
    /// <param name="imageSize">The original size of the image.</param>
    /// <param name="targetSize">The target size to scale to.</param>
    /// <param name="padding">The padding that defines the 9-slice boundary.</param>
    /// <returns>A tuple containing the vertex array and index array for the 9-slice mesh.</returns>
    public static (Vertex[], ushort[]) Make9SliceMeshData(
        Vector2 imageSize,
        Vector2 targetSize,
        Padding padding)
    {
        return Make9SliceMeshData(
            imageSize,
            targetSize,
            padding.Top,
            padding.Bottom,
            padding.Left,
            padding.Right);
    }

    /// <summary>
    /// Generate 9-slice mesh data for UI elements that need to scale without distorting corners and edges.
    /// </summary>
    /// <param name="imageSize">The original size of the image.</param>
    /// <param name="targetSize">The target size to scale to.</param>
    /// <param name="paddingTop">The top padding that defines the 9-slice boundary.</param>
    /// <param name="paddingBottom">The bottom padding that defines the 9-slice boundary.</param>
    /// <param name="paddingLeft">The left padding that defines the 9-slice boundary.</param>
    /// <param name="paddingRight">The right padding that defines the 9-slice boundary.</param>
    /// <returns>A tuple containing the vertex array and index array for the 9-slice mesh.</returns>
    public static (Vertex[], ushort[]) Make9SliceMeshData(
        Vector2 imageSize,
        Vector2 targetSize,
        float paddingTop,
        float paddingBottom,
        float paddingLeft,
        float paddingRight)
    {
        Vertex[] vertices = new Vertex[16];
        ushort[] indices = new ushort[54];

        Populate9SliceMeshData(vertices, indices, imageSize, targetSize, paddingTop, paddingBottom, paddingLeft, paddingRight);

        return (vertices, indices);
    }

    /// <summary>
    /// Populate existing vertex and index spans with 9-slice mesh data for UI elements that need to scale without distorting corners and edges.
    /// This method avoids memory allocation by directly modifying the provided spans.
    /// </summary>
    /// <param name="vertices">The vertex span to populate. Must have at least 16 elements.</param>
    /// <param name="indices">The index span to populate. Must have at least 54 elements.</param>
    /// <param name="imageSize">The original size of the image.</param>
    /// <param name="targetSize">The target size to scale to.</param>
    /// <param name="padding">The padding that defines the 9-slice boundary.</param>
    /// <exception cref="ArgumentException">Thrown when the provided spans are too small.</exception>
    public static void Populate9SliceMeshData(
        Span<Vertex> vertices,
        Span<ushort> indices,
        Vector2 imageSize,
        Vector2 targetSize,
        Padding padding)
    {
        Populate9SliceMeshData(
            vertices,
            indices,
            imageSize,
            targetSize,
            padding.Top,
            padding.Bottom,
            padding.Left,
            padding.Right);
    }

    /// <summary>
    /// Populate existing vertex and index spans with 9-slice mesh data for UI elements that need to scale without distorting corners and edges.
    /// This method avoids memory allocation by directly modifying the provided spans.
    /// </summary>
    /// <param name="vertices">The vertex span to populate. Must have at least 16 elements.</param>
    /// <param name="indices">The index span to populate. Must have at least 54 elements.</param>
    /// <param name="imageSize">The original size of the image.</param>
    /// <param name="targetSize">The target size to scale to.</param>
    /// <param name="paddingTop">The top padding that defines the 9-slice boundary.</param>
    /// <param name="paddingBottom">The bottom padding that defines the 9-slice boundary.</param>
    /// <param name="paddingLeft">The left padding that defines the 9-slice boundary.</param>
    /// <param name="paddingRight">The right padding that defines the 9-slice boundary.</param>
    /// <exception cref="ArgumentException">Thrown when the provided spans are too small.</exception>
    public static void Populate9SliceMeshData(
        Span<Vertex> vertices,
        Span<ushort> indices,
        Vector2 imageSize,
        Vector2 targetSize,
        float paddingTop,
        float paddingBottom,
        float paddingLeft,
        float paddingRight)
    {
        if (vertices.Length < 16)
            throw new ArgumentException("Vertex span must have at least 16 elements.", nameof(vertices));

        if (indices.Length < 54)
            throw new ArgumentException("Index span must have at least 54 elements.", nameof(indices));

        // Calculate normalized UV coordinates for the 9-slice boundaries
        float uvLeft = paddingLeft / imageSize.X;
        float uvRight = (imageSize.X - paddingRight) / imageSize.X;
        float uvTop = paddingTop / imageSize.Y;
        float uvBottom = (imageSize.Y - paddingBottom) / imageSize.Y;

        // Calculate world position coordinates for the 9-slice boundaries
        float halfTargetWidth = targetSize.X * 0.5f;
        float halfTargetHeight = targetSize.Y * 0.5f;

        float posLeft = -halfTargetWidth;
        float posRight = halfTargetWidth;
        float posTop = halfTargetHeight;
        float posBottom = -halfTargetHeight;

        float posInnerLeft = -halfTargetWidth + paddingLeft;
        float posInnerRight = halfTargetWidth - paddingRight;
        float posInnerTop = halfTargetHeight - paddingTop;
        float posInnerBottom = -halfTargetHeight + paddingBottom;

        // Generate 16 vertices in a 4x4 grid
        // Row 0 (top)
        vertices[0] = new Vertex(new Vector3(posLeft, posTop, 0), new Vector2(0, 0));
        vertices[1] = new Vertex(new Vector3(posInnerLeft, posTop, 0), new Vector2(uvLeft, 0));
        vertices[2] = new Vertex(new Vector3(posInnerRight, posTop, 0), new Vector2(uvRight, 0));
        vertices[3] = new Vertex(new Vector3(posRight, posTop, 0), new Vector2(1, 0));

        // Row 1 (inner top)
        vertices[4] = new Vertex(new Vector3(posLeft, posInnerTop, 0), new Vector2(0, uvTop));
        vertices[5] = new Vertex(new Vector3(posInnerLeft, posInnerTop, 0), new Vector2(uvLeft, uvTop));
        vertices[6] = new Vertex(new Vector3(posInnerRight, posInnerTop, 0), new Vector2(uvRight, uvTop));
        vertices[7] = new Vertex(new Vector3(posRight, posInnerTop, 0), new Vector2(1, uvTop));

        // Row 2 (inner bottom)
        vertices[8] = new Vertex(new Vector3(posLeft, posInnerBottom, 0), new Vector2(0, uvBottom));
        vertices[9] = new Vertex(new Vector3(posInnerLeft, posInnerBottom, 0), new Vector2(uvLeft, uvBottom));
        vertices[10] = new Vertex(new Vector3(posInnerRight, posInnerBottom, 0), new Vector2(uvRight, uvBottom));
        vertices[11] = new Vertex(new Vector3(posRight, posInnerBottom, 0), new Vector2(1, uvBottom));

        // Row 3 (bottom)
        vertices[12] = new Vertex(new Vector3(posLeft, posBottom, 0), new Vector2(0, 1));
        vertices[13] = new Vertex(new Vector3(posInnerLeft, posBottom, 0), new Vector2(uvLeft, 1));
        vertices[14] = new Vertex(new Vector3(posInnerRight, posBottom, 0), new Vector2(uvRight, 1));
        vertices[15] = new Vertex(new Vector3(posRight, posBottom, 0), new Vector2(1, 1));

        // Generate 54 indices for 9 quads (each quad uses 6 indices: 2 triangles)
        int indexOffset = 0;

        // Generate indices for each of the 9 quads in the 3x3 grid
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                // Calculate the base vertex index for this quad
                ushort baseIndex = (ushort)(row * 4 + col);

                // First triangle (top-left, top-right, bottom-left)
                indices[indexOffset++] = baseIndex;
                indices[indexOffset++] = (ushort)(baseIndex + 1);
                indices[indexOffset++] = (ushort)(baseIndex + 4);

                // Second triangle (top-right, bottom-right, bottom-left)
                indices[indexOffset++] = (ushort)(baseIndex + 1);
                indices[indexOffset++] = (ushort)(baseIndex + 5);
                indices[indexOffset++] = (ushort)(baseIndex + 4);
            }
        }
    }

    /// <summary>
    /// Populate existing vertex and index spans with tiled mesh data.
    /// The mesh will have UV coordinates that repeat based on the tile count.
    /// This method generates a simple quad with UV coordinates scaled for tiling.
    /// </summary>
    /// <param name="vertices">The vertex span to populate. Must have at least 4 elements.</param>
    /// <param name="indices">The index span to populate. Must have at least 6 elements.</param>
    /// <param name="imageSize">The original size of the image texture.</param>
    /// <param name="targetSize">The target size to render at.</param>
    /// <param name="tilingScale">The tiling scale multiplier. (1, 1) means natural tiling based on size/texture ratio.</param>
    /// <exception cref="ArgumentException">Thrown when the provided spans are too small.</exception>
    public static void PopulateTiledMeshData(
        Span<Vertex> vertices,
        Span<ushort> indices,
        Vector2 imageSize,
        Vector2 targetSize,
        Vector2 tilingScale)
    {
        if (vertices.Length < 4)
            throw new ArgumentException("Vertex span must have at least 4 elements.", nameof(vertices));

        if (indices.Length < 6)
            throw new ArgumentException("Index span must have at least 6 elements.", nameof(indices));

        // Calculate how many times the texture should repeat
        float tilesX = (targetSize.X / imageSize.X) * tilingScale.X;
        float tilesY = (targetSize.Y / imageSize.Y) * tilingScale.Y;

        // Calculate half dimensions for centering
        float halfWidth = targetSize.X * 0.5f;
        float halfHeight = targetSize.Y * 0.5f;

        // Create 4 vertices with UV coordinates scaled for tiling
        vertices[0] = new Vertex(new Vector3(-halfWidth, halfHeight, 0), new Vector2(0, 0));
        vertices[1] = new Vertex(new Vector3(halfWidth, halfHeight, 0), new Vector2(tilesX, 0));
        vertices[2] = new Vertex(new Vector3(halfWidth, -halfHeight, 0), new Vector2(tilesX, tilesY));
        vertices[3] = new Vertex(new Vector3(-halfWidth, -halfHeight, 0), new Vector2(0, tilesY));

        // Create 6 indices for 2 triangles
        indices[0] = 0;
        indices[1] = 1;
        indices[2] = 2;
        indices[3] = 0;
        indices[4] = 2;
        indices[5] = 3;
    }
}