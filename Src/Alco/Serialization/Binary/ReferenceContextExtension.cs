namespace Alco;

public static class ReferenceContextExtension
{
    public static void TryReadReferenceId(this ReferenceContext context, BinarySerializeReadNode node, ISerializable value)
    {
        if (value is IReferenceable referenceable
            && node.Content.TryGetValue(ReferenceContext.SerializeKey, out ulong id)
            && id != 0)
        {
            context.SetReference(id, referenceable);
        }
    }

    public static void TryWriteReferenceId(this ReferenceContext context, BinarySerializeWriteNode node, ISerializable value)
    {
        if (value is IReferenceable referenceable)
        {
            ulong id = context.GetId(referenceable);
            node.SetValue(ReferenceContext.SerializeKey, id);
        }
    }
}
