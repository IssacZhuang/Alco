using System;
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

    [Test]
    public void Foreach_Enumerates_InOrder()
    {
        var list = new UnorderedList<int>();
        for (int i = 0; i < 10; i++) list.Add(i);

        var seen = new List<int>();
        foreach (var v in list)
        {
            seen.Add(v);
        }

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, seen);
    }

    [Test]
    public void Foreach_Modify_During_Enumeration_Throws()
    {
        var list = new UnorderedList<int>();
        for (int i = 0; i < 5; i++) list.Add(i);

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var v in list)
            {
                if (v == 2)
                {
                    list.Add(99);
                }
            }
        });
    }
}

