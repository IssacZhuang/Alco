namespace Alco;

public interface ISerializable
{
    void OnSerialize(SerializeNode node, SerializeMode mode);
}