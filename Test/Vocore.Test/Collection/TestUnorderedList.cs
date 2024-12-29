namespace Vocore.Test;

public class TestUnorderedList
{
    [Test]
    public void Test()
    {
        var list = new UnorderedList<int>();

        int count = 1000000;
        for (int i = 0; i < count; i++)
        {
            list.Add(i);
        }

        for (int i = 0; i < count; i++)
        {
            list.Remove(i);
        }

        Assert.That(list.Count, Is.EqualTo(0));

        count = 10;
        for (int i = 0; i < count; i++)
        {
            list.Add(i);
        }

        list.RemoveAt(5);
        Assert.That(list.Count, Is.EqualTo(count - 1));
        Assert.That(list[5], Is.EqualTo(9));
    }
}

