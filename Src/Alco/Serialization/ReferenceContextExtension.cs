namespace Alco;

/// <summary>
/// Reads and writes serialized reference identifiers through a <see cref="ReferenceContext"/>.
/// </summary>
public static class ReferenceContextExtension
{
    /// <summary>
    /// Registers the deserialized value when it is referenceable and the node carries a non-zero reference identifier.
    /// </summary>
    /// <param name="context">The reference context to update.</param>
    /// <param name="node">The node being deserialized.</param>
    /// <param name="value">The deserialized value.</param>
    public static void TryReadReferenceId(this ReferenceContext context, SerializeReadNode node, ISerializable value)
    {
        if (value is not IReferenceable referenceable)
        {
            return;
        }

        ulong id = node.GetValue(ReferenceContext.SerializeKey, 0UL);
        if (id == 0)
        {
            return;
        }

        context.SetReference(id, referenceable);
    }

    /// <summary>
    /// Writes the reference identifier of the value when it is referenceable.
    /// </summary>
    /// <param name="context">The reference context that assigns identifiers.</param>
    /// <param name="node">The node being serialized.</param>
    /// <param name="value">The value being serialized.</param>
    public static void TryWriteReferenceId(this ReferenceContext context, SerializeWriteNode node, ISerializable value)
    {
        if (value is not IReferenceable referenceable)
        {
            return;
        }

        ulong id = context.GetId(referenceable);
        node.SetValue(ReferenceContext.SerializeKey, id);
    }
}
