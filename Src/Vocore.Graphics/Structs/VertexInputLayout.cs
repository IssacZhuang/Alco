namespace Vocore.Graphics;

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
}