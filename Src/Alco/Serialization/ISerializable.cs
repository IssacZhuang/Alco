namespace Alco;

/// <summary>
/// Defines an object that reads and writes its state through a <see cref="SerializeNode"/>.
/// </summary>
public interface ISerializable
{
    void OnSerialize(SerializeNode node, SerializeMode mode);
}