using Alco.GUI;

namespace Alco.GUI.Test;

/// <summary>
/// Unit tests for <see cref="UINode.FirstAncestorWhere"/>.
/// </summary>
[TestFixture]
public class UINodeFirstAncestorWhereTests
{
    /// <summary>Tests that the search matches the node itself when its predicate is true.</summary>
    [Test]
    public void MatchesSelf_WhenPredicateTrueForThis()
    {
        UINode node = new UINode { Name = "n" };

        UINode? result = node.FirstAncestorWhere(n => n == node);

        Assert.That(result, Is.SameAs(node));
    }

    /// <summary>Tests that the nearest matching ancestor is returned.</summary>
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

    /// <summary>Tests that the first matching node while walking up is returned.</summary>
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

    /// <summary>Tests that no match returns null.</summary>
    [Test]
    public void ReturnsNull_WhenNoMatch()
    {
        UINode node = new UINode { Name = "n" };

        UINode? result = node.FirstAncestorWhere(n => false);

        Assert.That(result, Is.Null);
    }
}
