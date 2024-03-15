namespace Vocore;

public interface ISerializable
{
    void OnSerialize(SerializeNode node, SerializeMode mode);
}