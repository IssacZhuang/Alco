namespace Alco;

public static class ReferenceContextExtension
{
    public static void TryReadReferenceId(this ReferenceContext context, BinarySerializeReadNode node, ISerializable value)
    {
        if (value is IReferenceable referenceable)
        {
            uint id = node.GetValue<uint>(ReferenceContext.SerializeKey);
            context.SetReference(id, referenceable);
        }
    }

    public static void TryWriteReferenceId(this ReferenceContext context, BinarySerializeWriteNode node, ISerializable value)
    {
        if (value is IReferenceable referenceable)
        {
            uint id = context.GetId(referenceable);
            node.SetValue(ReferenceContext.SerializeKey, id);
        }
    }
}