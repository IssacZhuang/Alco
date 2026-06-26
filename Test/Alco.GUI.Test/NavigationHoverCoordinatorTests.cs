using Alco.GUI;

namespace Alco.GUI.Test;

/// <summary>
/// Unit tests for <see cref="NavigationHoverCoordinator"/>: hover resolution,
/// hover-seeded navigation start, edge detection, and focus ownership.
/// </summary>
[TestFixture]
public class NavigationHoverCoordinatorTests
{
    /// <summary>
    /// A minimal <see cref="INavigationFocusable"/> owner for testing.
    /// </summary>
    private sealed class FakeFocusable : INavigationFocusable
    {
        public bool CanNavigate => true;
    }

    private static UINode MakeItem(string name)
    {
        var node = new UINode { Name = name };
        // Coordinator checks focused.IsEnable when deciding restore-hover.
        node.IsEnable = true;
        return node;
    }

    private static NavigationHoverCoordinator MakeVerticalList(
        FakeFocusable owner,
        List<UINode> items,
        out Func<int, UINode?> resolve,
        out Func<NavDirection, int, int?> navigate)
    {
        resolve = i => i >= 0 && i < items.Count ? items[i] : null;
        navigate = (direction, fromIndex) =>
        {
            int step = direction == NavDirection.Up ? -1 : 1;
            if (fromIndex < 0)
            {
                return step > 0 ? 0 : items.Count - 1;
            }
            int next = fromIndex + step;
            return next >= 0 && next < items.Count ? next : null;
        };

        return new NavigationHoverCoordinator(owner)
        {
            ResolveNode = resolve,
            TryNavigate = navigate,
            Orientation = NavOrientation.Vertical,
        };
    }

    // --- IsHoveredWithin ---

    [Test]
    public void IsHoveredWithin_ExactMatch_ReturnsTrue()
    {
        UINode target = MakeItem("target");

        Assert.That(NavigationHoverCoordinator.IsHoveredWithin(target, target), Is.True);
    }

    [Test]
    public void IsHoveredWithin_Descendant_ReturnsTrue()
    {
        UINode target = MakeItem("target");
        UINode child = MakeItem("child");
        UINode grandchild = MakeItem("grandchild");
        target.Add(child);
        child.Add(grandchild);

        Assert.That(NavigationHoverCoordinator.IsHoveredWithin(grandchild, target), Is.True);
    }

    [Test]
    public void IsHoveredWithin_UnrelatedNode_ReturnsFalse()
    {
        UINode target = MakeItem("target");
        UINode other = MakeItem("other");

        Assert.That(NavigationHoverCoordinator.IsHoveredWithin(other, target), Is.False);
    }

    [Test]
    public void IsHoveredWithin_NullHovered_ReturnsFalse()
    {
        UINode target = MakeItem("target");

        Assert.That(NavigationHoverCoordinator.IsHoveredWithin(null, target), Is.False);
    }

    // --- Focus basics ---

    [Test]
    public void SetFocus_NegativeOne_ClearsFocus()
    {
        var owner = new FakeFocusable();
        var items = new List<UINode> { MakeItem("a"), MakeItem("b") };
        var nav = MakeVerticalList(owner, items, out _, out _);

        nav.SetFocus(1);

        nav.SetFocus(-1);

        Assert.Multiple(() =>
        {
            Assert.That(nav.FocusedIndex, Is.EqualTo(-1));
            Assert.That(nav.FocusedNode, Is.Null);
        });
    }

    [Test]
    public void ClearFocus_ResetsIndex()
    {
        var owner = new FakeFocusable();
        var items = new List<UINode> { MakeItem("a"), MakeItem("b") };
        var nav = MakeVerticalList(owner, items, out _, out _);

        nav.SetFocus(0);
        nav.ClearFocus();

        Assert.That(nav.FocusedIndex, Is.EqualTo(-1));
    }

    // --- Edge detection / navigation ---

    [Test]
    public void Tick_OwnerNotActive_ClearsFocusAndIgnoresInput()
    {
        var owner = new FakeFocusable();
        var items = new List<UINode> { MakeItem("a"), MakeItem("b"), MakeItem("c") };
        var nav = MakeVerticalList(owner, items, out _, out _);
        var tracker = new FakeInputTracker();
        var ctx = new FakeNavigationContext(tracker) { NavigationFocus = null };

        nav.SetFocus(0);
        tracker.IsKeyDownPressing = true;
        nav.Tick(ctx); // NavigationFocus != owner => focus cleared, no navigation

        Assert.That(nav.FocusedIndex, Is.EqualTo(-1));
    }

    [Test]
    public void Tick_OwnerInactive_ClearsFocus()
    {
        var owner = new FakeFocusable();
        var items = new List<UINode> { MakeItem("a"), MakeItem("b") };
        var nav = MakeVerticalList(owner, items, out _, out _);
        var tracker = new FakeInputTracker();
        var ctx = new FakeNavigationContext(tracker) { NavigationFocus = null };

        nav.SetFocus(1);

        nav.Tick(ctx); // owner not active -> focus cleared

        Assert.That(nav.FocusedIndex, Is.EqualTo(-1));
    }

    [Test]
    public void Tick_HeldDirection_OnlyFiresOncePerEdge()
    {
        var owner = new FakeFocusable();
        var items = new List<UINode> { MakeItem("a"), MakeItem("b"), MakeItem("c"), MakeItem("d") };
        var nav = MakeVerticalList(owner, items, out _, out _);
        var tracker = new FakeInputTracker();
        var ctx = new FakeNavigationContext(tracker) { NavigationFocus = owner };

        nav.SetFocus(0);

        tracker.IsKeyDownPressing = true;
        nav.Tick(ctx); // rising edge: 0 -> 1
        nav.Tick(ctx); // held, no new edge: stays at 1

        Assert.That(nav.FocusedIndex, Is.EqualTo(1));
    }

    // --- Hover-seeded navigation start ---

    [Test]
    public void Tick_NoFocus_HoverOnNavigableChild_StartsFromHover()
    {
        var owner = new FakeFocusable();
        var items = new List<UINode> { MakeItem("0"), MakeItem("1"), MakeItem("2"), MakeItem("3"), MakeItem("4"), MakeItem("5") };
        var nav = MakeVerticalList(owner, items, out _, out _);
        nav.IndexOfHoveredChild = hovered =>
        {
            if (hovered == null) return null;
            for (int i = 0; i < items.Count; i++)
            {
                if (NavigationHoverCoordinator.IsHoveredWithin(hovered, items[i])) return i;
            }
            return null;
        };

        var tracker = new FakeInputTracker();
        var ctx = new FakeNavigationContext(tracker) { NavigationFocus = owner, Hovered = items[5] };

        tracker.IsKeyDownPressing = true;
        nav.Tick(ctx);

        // Hovered is item 5; pressing Down at the last index clamps and returns null
        // (no move possible), so focus stays -1. Use Up to verify seeding works.
        Assert.That(nav.FocusedIndex, Is.EqualTo(-1));
    }

    [Test]
    public void Tick_NoFocus_HoverOnNavigableChild_PressingUp_MovesUpFromHover()
    {
        var owner = new FakeFocusable();
        var items = new List<UINode> { MakeItem("0"), MakeItem("1"), MakeItem("2"), MakeItem("3"), MakeItem("4"), MakeItem("5") };
        var nav = MakeVerticalList(owner, items, out _, out _);
        nav.IndexOfHoveredChild = hovered =>
        {
            if (hovered == null) return null;
            for (int i = 0; i < items.Count; i++)
            {
                if (NavigationHoverCoordinator.IsHoveredWithin(hovered, items[i])) return i;
            }
            return null;
        };

        var tracker = new FakeInputTracker();
        var ctx = new FakeNavigationContext(tracker) { NavigationFocus = owner, Hovered = items[5] };

        tracker.IsKeyUpPressing = true;
        nav.Tick(ctx);

        // Seeded from item 5, Up -> item 4.
        Assert.That(nav.FocusedIndex, Is.EqualTo(4));
    }

    [Test]
    public void Tick_NoFocus_HoverOutsideList_FallsBackToEdge()
    {
        var owner = new FakeFocusable();
        var items = new List<UINode> { MakeItem("0"), MakeItem("1"), MakeItem("2") };
        var nav = MakeVerticalList(owner, items, out _, out _);
        nav.IndexOfHoveredChild = hovered =>
        {
            if (hovered == null) return null;
            for (int i = 0; i < items.Count; i++)
            {
                if (NavigationHoverCoordinator.IsHoveredWithin(hovered, items[i])) return i;
            }
            return null;
        };

        var tracker = new FakeInputTracker();
        // Hovered is an unrelated node not in the list.
        var ctx = new FakeNavigationContext(tracker) { NavigationFocus = owner, Hovered = MakeItem("outside") };

        tracker.IsKeyDownPressing = true;
        nav.Tick(ctx);

        // No hover seed -> fall back to first item.
        Assert.That(nav.FocusedIndex, Is.EqualTo(0));
    }

    [Test]
    public void Tick_AlreadyFocused_ButHoveredElsewhere_StartsFromHover()
    {
        var owner = new FakeFocusable();
        var items = new List<UINode> { MakeItem("0"), MakeItem("1"), MakeItem("2"), MakeItem("3") };
        var nav = MakeVerticalList(owner, items, out _, out _);
        nav.IndexOfHoveredChild = hovered =>
        {
            if (hovered == null) return null;
            for (int i = 0; i < items.Count; i++)
            {
                if (NavigationHoverCoordinator.IsHoveredWithin(hovered, items[i])) return i;
            }
            return null;
        };

        var tracker = new FakeInputTracker();
        var ctx = new FakeNavigationContext(tracker) { NavigationFocus = owner, Hovered = items[2] };

        nav.SetFocus(0);
        tracker.IsKeyDownPressing = true;
        nav.Tick(ctx);

        // Focus is 0, but cursor is over item 2 -> navigation starts from 2 -> 3.
        Assert.That(nav.FocusedIndex, Is.EqualTo(3));
    }

    // --- Post-navigation hover application ---

    [Test]
    public void Tick_Navigation_AppliesHoverToNewFocused()
    {
        var owner = new FakeFocusable();
        var items = new List<UINode> { MakeItem("0"), MakeItem("1"), MakeItem("2") };
        var nav = MakeVerticalList(owner, items, out _, out _);
        var tracker = new FakeInputTracker();
        var ctx = new FakeNavigationContext(tracker) { NavigationFocus = owner };

        nav.SetFocus(0);
        tracker.IsKeyDownPressing = true;
        nav.Tick(ctx);

        Assert.Multiple(() =>
        {
            Assert.That(ctx.LastSetHovered, Is.SameAs(items[1]));
            Assert.That(nav.FocusedIndex, Is.EqualTo(1));
        });
    }
}
