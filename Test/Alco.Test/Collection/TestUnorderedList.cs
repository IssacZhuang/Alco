using System.Collections.Generic;

namespace Alco.Test;

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

        list.Clear();
        HashSet<int> pendingRemove = [0, 5, 6, 9];

        UnorderedList<bool> list2 = new();
        for (int i = 0; i < count; i++)
        {
            if (pendingRemove.Contains(i))
            {
                list2.Add(true);
            }
            else
            {
                list2.Add(false);
            }
        }

        for (int i = 0; i < list2.Count; i++)
        {
            if (list2[i])
            {
                list2.RemoveAt(i);
                i--;
            }
        }

        Assert.That(list2.Count, Is.EqualTo(count - pendingRemove.Count));
    }
}

