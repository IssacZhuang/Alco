using System.Text;

namespace Alco.Graphics;


/// <summary>
/// Describes the layout of vertex data in a buffer. It also describes the vertex to fragment shader input.
/// </summary>
public struct VertexInputLayout
{
    /// <summary>
    /// Bytes between consecutive vertices (or instances) in the buffer.
    /// </summary>
    public uint Stride;
    /// <summary>
    /// How often the binding advances to its next element.
    /// </summary>
    public VertexStepMode StepMode;
    /// <summary>
    /// The attributes fetched from one vertex of the buffer.
    /// </summary>
    public VertexElement[] Elements;

    /// <summary>
    /// Initializes the layout with its attributes, stride and step mode.
    /// </summary>
    public VertexInputLayout(VertexElement[] elements, uint stride, VertexStepMode stepMode)
    {
        Elements = elements;
        Stride = stride;
        StepMode = stepMode;
    }

    public override string ToString()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine($"[Vertex Layout] Stride: {Stride}, StepMode: {StepMode}");
        foreach (var element in Elements)
        {
            builder.AppendLine(element.ToString());
        }
        return builder.ToString();
    }
}