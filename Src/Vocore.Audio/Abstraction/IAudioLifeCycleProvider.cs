namespace Vocore.Audio;

public interface IAudioLifeCycleProvider
{
    event Action OnDispose;
}