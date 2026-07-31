using Alco.GUI;

namespace Alco.GUI.Test;

/// <summary>
/// Unit tests for <see cref="UINode.FirstAncestorWhere"/>.
/// </summary>
[TestFixture]
public class UINodeFirstAncestorWhereTests
{
    [Test]
    public void MatchesSelf_WhenPredicateTrueForThis()
    {
        UINode node = new UINode { Name = "n" };

        UINode? result = node.FirstAncestorWhere(n => n == node);

        Assert.That(result, Is.SameAs(node));
    }

    [Test]
    public void ReturnsParent_WhenPredicateMatchesAncestor()
    {
        UINode root = new UINode { Name = "root" };
        UINode mid = new UINode { Name = "mid" };
        UINode leaf = new UINode { Name = "leaf" };
        root.Add(mid);
        mid.Add(leaf);

        UINode? result = leaf.FirstAncestorWhere(n => n == root);

        Assert.That(result, Is.SameAs(root));
    }

    [Test]
    public void ReturnsFirstMatching_WhenMultipleMatch()
    {
        UINode root = new UINode { Name = "root" };
        UINode mid = new UINode { Name = "mid" };
        UINode leaf = new UINode { Name = "leaf" };
        root.Add(mid);
        mid.Add(leaf);

        UINode? result = leaf.FirstAncestorWhere(n => n.Name.StartsWith('m') || n.Name.StartsWith('r'));

        // leaf itself doesn't match; mid matches first walking up.
        Assert.That(result, Is.SameAs(mid));
    }

    [Test]
    public void ReturnsNull_WhenNoMatch()
    {
        UINode node = new UINode { Name = "n" };

        UINode? result = node.FirstAncestorWhere(n => false);

        Assert.That(result, Is.Null);
    }
}
