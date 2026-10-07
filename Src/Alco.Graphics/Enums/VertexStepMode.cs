namespace Alco.Graphics;

/// <summary>
/// How often a vertex buffer binding advances to its next element.
/// </summary>
public enum VertexStepMode
{
    /// <summary>One element per vertex.</summary>
    Vertex = 0,
    /// <summary>One element per instance; all vertices of an instance share the element.</summary>
    Instance = 1,
    /// <summary>The buffer is not used by the pipeline.</summary>
    NotUsed = 2,
}