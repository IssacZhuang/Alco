using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public class UILabel : UINode
{
    public const float TextOffsetYMultiplier = 0.125f;
    private readonly ArrayBuffer<char> _text = new ArrayBuffer<char>(); // for less GC
    private int _textLength;
    private string _tmpStrForRead = string.Empty;
    private bool _isTmpStrForReadDirty;
    private Pivot _textPivot = Pivot.Center; // the pivot of the text relative to the container

    public Font? FontOverride { get; set; }
    public float FontSize { get; set; } = 16;
    public ColorFloat Color { get; set; } = 0xffffff;

    public string Text
    {
        get
        {
            if (_isTmpStrForReadDirty)
            {
                _tmpStrForRead = new string(_text.Span);
                _isTmpStrForReadDirty = false;
            }
            return _tmpStrForRead;
        }
        set
        {
            SetText(value);
        }
    }

    public Pivot TextPivot
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _textPivot;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _textPivot = value;
    }

    public TextAlignVertical AlignVertical
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _textPivot.Y = UtilsTextAlign.GetPivotY(value);

    }

    public TextAlignHorizontal AlignHorizontal
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _textPivot.X = UtilsTextAlign.GetPivotX(value);

    }

    public UILabel()
    {

    }

    protected unsafe override void OnUpdate(Canvas canvas, float delta)
    {
        CanvasRenderer renderer = canvas.Renderer;
        Transform2D transform = WorldTransform;
        transform.position += transform.scale * Size * TextPivot;
        
        transform.scale *= FontSize;
        transform.position.Y += transform.scale.Y * TextOffsetYMultiplier;

        Font font = FontOverride ?? canvas.Font;

        BoundingBox2D mask = Mask;
        if (!HasMask)
        {
            mask = canvas.Bound;
        }

        renderer.DrawChars(font, _text.Slice(0, _textLength), transform.Matrix, _textPivot, Color, 1f, mask);
    }

    public void SetText(string str)
    {
        _tmpStrForRead = str;
        _text.EnsureSizeWithoutCopy(str.Length);
        _textLength = str.Length;
        for (int i = 0; i < str.Length; i++)
        {
            _text[i] = str[i];
        }
    }

    public void SetText(ReadOnlySpan<char> str)
    {
        _isTmpStrForReadDirty = true;
        _text.EnsureSizeWithoutCopy(str.Length);
        _textLength = str.Length;
        for (int i = 0; i < str.Length; i++)
        {
            _text[i] = str[i];
        }
    }

    public unsafe void SetText(char* str, int length)
    {
        _isTmpStrForReadDirty = true;
        _text.EnsureSizeWithoutCopy(length);
        _textLength = length;
        for (int i = 0; i < length; i++)
        {
            _text[i] = str[i];
        }
    }


}