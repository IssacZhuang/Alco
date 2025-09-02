using System.Numerics;
using Alco;
using Alco.GUI;
using Alco.Rendering;

/// <summary>
/// A list item for integers. It is a button with a text label showing the number.
/// </summary>
public class IntListItem : UIButton, IUIListItem<int>
{
	private readonly UISprite _background;
	private readonly UIText _label;

	/// <summary>
	/// Creates a new integer list item with a background and centered text.
	/// </summary>
	public IntListItem()
	{
		// Use center anchor for consistent layout calculation
		Anchor = Anchor.Center;
		Size = new Vector2(100, 24);

		_background = new UISprite
		{
			Anchor = Anchor.Stretch,
			Color = 0x2a2a2a,
			SizeDelta = Vector2.Zero
		};

		_label = new UIText
		{
			Anchor = Anchor.Stretch,
			AlignHorizontal = TextAlign.Center,
			AlignVertical = TextAlign.Center,
			Color = 0xffffff,
			FontSize = 16,
			SizeDelta = Vector2.Zero
		};

		TransitionMode = TransitionMode.ColorTint;
		TransitionTarget = _background;
		ColorNormal = 0x2a2a2a;
		ColorHover = 0x3a3a3a;
		ColorPressing = 0x234A6C;

		Add(_background, false);
		Add(_label, false);
	}

	/// <summary>
	/// Sets or gets the label font of this item.
	/// </summary>
	public Font? LabelFont
	{
		get => _label.Font;
		set => _label.Font = value;
	}

	/// <summary>
	/// Binds the integer data to this item by setting the label text.
	/// </summary>
	/// <param name="index">The item index.</param>
	/// <param name="data">The integer data.</param>
	public void SetData(int index, int data)
	{
		FixedString32 text = new FixedString32();
		text.Append(data);
		_label.SetText(text);
	}
}


