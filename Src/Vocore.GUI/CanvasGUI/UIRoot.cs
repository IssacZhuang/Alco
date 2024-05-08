namespace Vocore.GUI;

public class UIRoot : UINode
{
    public override void OnTick(float delta)
    {

    }

    public override void OnUpdate(float delta)
    {

    }

    public void UpdateChild()
    {
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].InternalUpdate(0);
        }
    }
}