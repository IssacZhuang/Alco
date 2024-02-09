using System.Text;

namespace Vocore.Graphics;


/// <summary>
/// Describes the layout of vertex data in a buffer. It also describes the vertex to fragment shader input.
/// </summary>
public struct VertexInputLayout
{
    public uint Stride;
    public VertexStepMode StepMode;
    public VertexElement[] Elements;

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