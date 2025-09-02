using System.Collections.Generic;
using System.Numerics;
using Alco;
using Alco.GUI;
using Alco.Rendering;

/// <summary>
/// A concrete UIList for integers, producing IntListItem buttons.
/// </summary>
public class IntList : UIList<int>
{
	/// <summary>
	/// Optional font to apply to each created list item.
	/// </summary>
	public Font? ItemFont { get; set; }

	/// <summary>
	/// Creates a new IntList with sensible defaults for item spacing and height.
	/// </summary>
	public IntList()
	{
		this.Anchor = Anchor.Stretch;
		Spacing = 4f;
		IsFixedItemHeight = true;
		FixedItemHeight = 28f;
	}

	protected override UINode CreateItem()
	{
		var item = new IntListItem();
		if (ItemFont != null)
		{
			item.LabelFont = ItemFont;
		}
		return item;
	}
}


