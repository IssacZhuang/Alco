namespace Vocore.Engine;

internal sealed class NoWindow : Window
{
    public override WindowMode WindowMode { get; set; }
    public override int2 Size { get; set; }
    public override float AspectRatio { get; }
    public override string Title { get; set; } = "No Window";

    internal override void Close()
    {
        
    }
}
