namespace Alco.Audio.OpenAL;

/// <summary>
/// Implemented by any object that borrows an OpenAL source from the device <c>SourcePool</c>.
/// Used so the pool can notify an owner when its borrowed source is reclaimed (e.g. because the
/// source stopped and another borrower needs it), letting the owner drop its reference to that id.
/// </summary>
internal interface IOpenALSourceOwner
{
    /// <summary>
    /// Called by the pool when the source previously allocated to this owner is being handed to a
    /// different owner. The implementation must stop referencing that source id and clear any
    /// state bound to it (buffers, queue, etc.).
    /// </summary>
    void OnSourceReclaimed();
}
