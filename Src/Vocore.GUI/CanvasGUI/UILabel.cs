using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public class UILabel : UINode
{
    private ArrayBuffer<char> _text = new ArrayBuffer<char>(); // for less GC
    private int _textLength;
    private string _tmpStrForRead = string.Empty;
    private bool _isTmpStrForReadDirty;

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

    public UILabel()
    {

    }

    protected unsafe override void OnUpdate(Canvas canvas, float delta)
    {
        CanvasRenderer renderer = canvas.Renderer;
        Transform2D transform = WorldTransform;
        transform.scale *= FontSize;
        renderer.DrawChars(canvas.Font, _text.Slice(0, _textLength), transform.Matrix, Pivot.Center, Color);
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