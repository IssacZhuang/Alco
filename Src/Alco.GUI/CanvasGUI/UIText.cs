using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

/// <summary>
/// The label UI node.
/// </summary>
public class UIText : UISelectable
{
    /// <summary>The minimum temporary buffer size for span-formatted text.</summary>
    public const int MinSpanFormattableSize = 32;
    protected struct Line
    {
        public int start;
        public int count;
        public float width;
    }
    private readonly ArrayBuffer<char> _text = new ArrayBuffer<char>(); // for less GC
    protected readonly List<Line> _lines = new List<Line>();
    private bool _isLineBreakDirty;
    private int _textLength;
    private float _fontSize = 16f;
    private string _tmpStr = string.Empty;
    private bool _isTmpStrReadDirty;
    private bool _isTmpStrWriteDirty;
    private Pivot _textPivot = Pivot.Center; // the pivot of the text relative to the container
    private OverflowModeHorizontal _overflowHorizontal = OverflowModeHorizontal.None;
    private OverflowModeVertical _overflowVertical = OverflowModeVertical.None;
    private bool _isRichText;
    // rich text buffers, lazily created when IsRichText is first enabled
    private ArrayBuffer<char>? _richText;
    private int _richTextLength;
    private List<TextSlice>? _richSlices;
    private ArrayBuffer<TextSlice>? _lineSlices;
    private MaterialAsset? _material;
    private GraphicsMaterialInstance? _materialInstance;
    private Canvas? _materialCanvas;

    /// <summary>
    /// The font for rendering text. The text will not display if the font is null.
    /// </summary>
    /// <value></value>
    public Font? Font { get; set; }

    /// <summary>
    /// The material asset whose surface shades the glyphs instead of the plain text
    /// material. The surface must implement the engine's <c>IUISurface</c> protocol
    /// and return straight RGBA; the text pass applies glyph coverage and
    /// premultiplies the output.
    /// </summary>
    public MaterialAsset? Material
    {
        get => _material;
        set
        {
            if (_material == value)
            {
                return;
            }
            _material = value;
            DisposeMaterialInstance();
        }
    }

    /// <summary>
    /// The font size for rendering text.
    /// </summary>
    /// <value></value>
    public float FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            if (_overflowHorizontal == OverflowModeHorizontal.NextLine || _fitContentMode != FitContentMode.None)
            {
                TryRefreshTextLineBreak();
            }
        }
    }

    /// <summary>
    /// The normalized line spacing, the line height is FontSize * LineSpacing.
    /// </summary>
    /// <value></value>
    public float LineSpacing { get; set; } = 1f;

    /// <summary>
    /// Controls which axis auto-adjusts to match the text content size; <see cref="FitContentMode.Width"/> is ignored when <see cref="OverflowHorizontal"/> is NextLine.
    /// </summary>
    public FitContentMode FitContentMode
    {
        get => _fitContentMode;
        set
        {
            _fitContentMode = value;
            if (_fitContentMode != FitContentMode.None)
            {
                SetLineBreakDirty();
            }
        }
    }
    private FitContentMode _fitContentMode = FitContentMode.None;


    /// <summary>
    /// The text data.
    /// </summary>
    /// <value></value>
    public Span<char> TextSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _text.AsSpan(0, _textLength);
        }
    }

    /// <summary>
    /// The text string.
    /// </summary>
    /// <value></value>
    public string Text
    {
        get
        {
            // A pending unflushed write always wins: rebuilding _tmpStr from the flushed buffer
            // here would silently discard the write (set -> get before OnUpdate flush).
            if (_isTmpStrWriteDirty)
            {
                return _tmpStr;
            }

            if (_isTmpStrReadDirty)
            {
                _tmpStr = new string(_text.AsSpan(0, _textLength));
                _isTmpStrReadDirty = false;
            }
            return _tmpStr;
        }
        set
        {
            //SetText(value);
            _tmpStr = value;
            _isTmpStrWriteDirty = true;
        }
    }

    /// <summary>
    /// Enables rich text parsing of &lt;color&gt; tags, which tint the enclosed characters and are stripped from the rendered text; malformed tags render as plain text.
    /// </summary>
    public bool IsRichText
    {
        get => _isRichText;
        set
        {
            if (_isRichText == value)
            {
                return;
            }
            _isRichText = value;
            if (value)
            {
                _richText ??= new ArrayBuffer<char>();
                _richSlices ??= new List<TextSlice>();
                _lineSlices ??= new ArrayBuffer<TextSlice>();
            }
            SetLineBreakDirty();
        }
    }

    /// <summary>
    /// The text pivot relative to the self container.
    /// </summary>
    /// <value></value>
    public Pivot TextPivot
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _textPivot;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _textPivot = value;
    }

    /// <summary>
    /// Controls how text that exceeds the node width is laid out.
    /// </summary>
    public OverflowModeHorizontal OverflowHorizontal
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _overflowHorizontal;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _overflowHorizontal = value;
    }

    /// <summary>
    /// Controls how text that exceeds the node height is handled.
    /// </summary>
    public OverflowModeVertical OverflowVertical
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _overflowVertical;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _overflowVertical = value;
    }

    /// <summary>
    /// The vertical alignment of text inside the node.
    /// </summary>
    public TextAlign AlignVertical
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _textPivot.Y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _textPivot.Y = value;

    }

    /// <summary>
    /// The horizontal alignment of text inside the node.
    /// </summary>
    public TextAlign AlignHorizontal
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _textPivot.X;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _textPivot.X = value;
    }

    /// <summary>
    /// Creates a non-interactable text node.
    /// </summary>
    public UIText()
    {
        //default false, override by InputBox
        IsInteractable = false;
    }

    /// <summary>
    /// Gets this node's material instance, creating it with the canvas camera and
    /// stencil state when needed. Overrides on the returned instance do not modify
    /// the shared base material and are preserved while the instance is reused.
    /// </summary>
    /// <param name="canvas">The canvas that will draw the text.</param>
    /// <returns>The instance used for drawing, valid until the material or canvas
    /// changes, the node detaches, or the instance is disposed.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Material"/> is null.</exception>
    public GraphicsMaterialInstance GetMaterialInstance(Canvas canvas)
    {
        if (_material == null)
        {
            throw new InvalidOperationException($"UIText '{Name}': Material must be set before requesting a material instance.");
        }

        if (_materialInstance == null || _materialInstance.IsDisposed || !ReferenceEquals(_materialCanvas, canvas))
        {
            DisposeMaterialInstance();
            _materialInstance = canvas.CreateUIMaterialInstance(canvas.GetTextMaterial(_material));
            _materialCanvas = canvas;
        }

        return _materialInstance;
    }

    private void DisposeMaterialInstance()
    {
        _materialInstance?.Dispose();
        _materialInstance = null;
        _materialCanvas = null;
    }

    protected override void OnDetachFromTree(Canvas canvas)
    {
        DisposeMaterialInstance();
        base.OnDetachFromTree(canvas);
    }

    protected unsafe override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);
        if (Font == null)
        {
            return;
        }

        if (_material != null)
        {
            GetMaterialInstance(canvas);
        }

        if (_isTmpStrWriteDirty)
        {
            SetText(_tmpStr);
        }

        if (_isLineBreakDirty)
        {
            TryRefreshTextLineBreak();
        }

        Span<char> renderText = RenderTextSpan;
        if (renderText.Length == 0)
        {
            return;
        }


        //use local transform
        Transform2D transform = Transform2D.Identity;
        transform.Position = Size * TextPivot;
        transform.Scale = new Vector2(FontSize);
        float lineHeight = LineSpacing* FontSize;
        float offsetY = (_lines.Count - 1) * lineHeight * (0.5f - TextPivot.Y);
        transform.Position.Y += offsetY;


        for (int i = 0; i < _lines.Count; i++)
        {
            DrawLine(canvas, i, renderText.Slice(_lines[i].start, _lines[i].count), transform);
            transform.Position.Y -= lineHeight;
        }
    }

    /// <summary>
    /// Copies a string into the text buffer and marks its layout dirty.
    /// </summary>
    /// <param name="str">The text to display.</param>
    public unsafe void SetText(string str)
    {
        fixed (char* p = str)
        {
            SetText(p, str.Length);
        }
    }

    /// <summary>
    /// Copies a character span into the text buffer and marks its layout dirty.
    /// </summary>
    /// <param name="str">The text to display.</param>
    public unsafe void SetText(ReadOnlySpan<char> str)
    {
        fixed (char* p = str)
        {
            SetText(p, str.Length);
        }
    }

    /// <summary>
    /// Copies characters from a pointer into the text buffer and marks its layout dirty.
    /// </summary>
    /// <param name="str">The pointer to the characters to display.</param>
    /// <param name="length">The number of characters to copy.</param>
    public unsafe void SetText(char* str, int length)
    {
        _isTmpStrReadDirty = true;
        _isTmpStrWriteDirty = false;
        _text.SetSizeWithoutCopy(length);

        _textLength = length;
        for (int i = 0; i < length; i++)
        {
            _text[i] = str[i];
        }

        SetLineBreakDirty();
    }

    protected override void OnAttachToTree(Canvas canvas)
    {
        Font ??= canvas.DefaultFont;
    }

    protected virtual void DrawLine(Canvas canvas, int line, ReadOnlySpan<char> chars, Transform2D textLineTransform)
    {
        if (_isRichText && _richSlices!.Count > 0)
        {
            canvas.DrawChars(Font!, chars, math.transform(WorldTransform, textLineTransform).Matrix, TextPivot, RenderColor, GetLineSlices(line), 1f, _materialInstance);
            return;
        }
        canvas.DrawChars(Font!, chars, math.transform(WorldTransform, textLineTransform).Matrix, TextPivot, RenderColor, 1f, _materialInstance);
    }

    /// <summary>
    /// The characters that are actually rendered: the rich-text-stripped text when
    /// <see cref="IsRichText"/> is enabled, otherwise the raw text.
    /// <see cref="_lines"/> indices refer to this span.
    /// </summary>
    protected Span<char> RenderTextSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isRichText ? _richText!.AsSpan(0, _richTextLength) : _text.AsSpan(0, _textLength);
    }

    // clips the parsed color slices to the given line and rebases them to line-local indices
    private ReadOnlySpan<TextSlice> GetLineSlices(int line)
    {
        Line textLine = _lines[line];
        int lineStart = textLine.start;
        int lineEnd = lineStart + textLine.count;
        ColorFloat renderColor = RenderColor;

        List<TextSlice> slices = _richSlices!;
        ArrayBuffer<TextSlice> lineSlices = _lineSlices!;
        lineSlices.SetSizeWithoutCopy(slices.Count);

        int count = 0;
        for (int i = 0; i < slices.Count; i++)
        {
            TextSlice slice = slices[i];
            int sliceEnd = slice.Start + slice.Length;
            if (sliceEnd <= lineStart)
            {
                continue;
            }
            if (slice.Start >= lineEnd)
            {
                break; // slices are sorted by start
            }
            int clippedStart = Math.Max(slice.Start, lineStart);
            int clippedEnd = Math.Min(sliceEnd, lineEnd);
            lineSlices[count++] = new TextSlice
            {
                Color = slice.Color * renderColor,
                Start = clippedStart - lineStart,
                Length = clippedEnd - clippedStart
            };
        }
        return lineSlices.AsSpan(0, count);
    }

    private const string RichTextColorOpenPrefix = "<color=";
    private const string RichTextColorCloseTag = "</color>";
    private const int MaxRichTextColorDepth = 16;

    // strips <color> tags from the raw text into _richText and records color runs in _richSlices
    private void ParseRichText()
    {
        ReadOnlySpan<char> input = TextSpan;
        _richText!.SetSizeWithoutCopy(input.Length);
        _richSlices!.Clear();

        Span<ColorFloat> colorStack = stackalloc ColorFloat[MaxRichTextColorDepth];
        int depth = 0;
        int outLength = 0;
        int runStart = 0;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '<')
            {
                if (depth < MaxRichTextColorDepth && TryParseColorOpenTag(input.Slice(i), out int tagLength, out ColorFloat tagColor))
                {
                    FlushColorRun(colorStack, depth, runStart, outLength);
                    colorStack[depth++] = tagColor;
                    runStart = outLength;
                    i += tagLength - 1;
                    continue;
                }
                if (depth > 0 && input.Slice(i).StartsWith(RichTextColorCloseTag))
                {
                    FlushColorRun(colorStack, depth, runStart, outLength);
                    depth--;
                    runStart = outLength;
                    i += RichTextColorCloseTag.Length - 1;
                    continue;
                }
            }
            _richText[outLength++] = c;
        }
        FlushColorRun(colorStack, depth, runStart, outLength);
        _richTextLength = outLength;
    }

    private void FlushColorRun(ReadOnlySpan<ColorFloat> colorStack, int depth, int runStart, int outLength)
    {
        if (depth <= 0 || outLength <= runStart)
        {
            return;
        }
        _richSlices!.Add(new TextSlice
        {
            Color = colorStack[depth - 1],
            Start = runStart,
            Length = outLength - runStart
        });
    }

    private static bool TryParseColorOpenTag(ReadOnlySpan<char> input, out int tagLength, out ColorFloat color)
    {
        tagLength = 0;
        color = default;
        if (!input.StartsWith(RichTextColorOpenPrefix))
        {
            return false;
        }
        ReadOnlySpan<char> value = input.Slice(RichTextColorOpenPrefix.Length);
        int closeIndex = value.IndexOf('>');
        if (closeIndex <= 0 || !ColorFloat.TryParse(value.Slice(0, closeIndex), out color))
        {
            return false;
        }
        tagLength = RichTextColorOpenPrefix.Length + closeIndex + 1;
        return true;
    }

    protected void SetLineBreakDirty()
    {
        _isLineBreakDirty = true;
    }

    protected void TryRefreshTextLineBreak()
    {
        if (!_isLineBreakDirty)
        {
            return;
        }
        _isLineBreakDirty = false;
        if (_isRichText)
        {
            ParseRichText();
        }
        Span<char> text = RenderTextSpan;
        _lines.Clear();

        if (text.Length == 0)
        {
            return;
        }

        int currentLineStart = 0;
        int currentLineCount = 0;
        float currentLineWidth = 0;
        int lastBreakIndex = -1;
        float lastBreakWidth = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            GlyphInfo glyph = Font!.GetGlyph(c);
            float charWidth = glyph.Advance;

            if (c == '\n' || c == '\r')
            {
                currentLineCount++;
                _lines.Add(new Line
                {
                    start = currentLineStart,
                    count = currentLineCount,
                    width = currentLineWidth
                });

                currentLineStart = i + 1;
                currentLineCount = 0;
                currentLineWidth = 0;
                lastBreakIndex = -1;
                continue;
            }

            if (_overflowHorizontal == OverflowModeHorizontal.NextLine && (currentLineWidth + charWidth) * _fontSize > Size.X)
            {
                if (lastBreakIndex != -1 && lastBreakIndex >= currentLineStart)
                {
                    _lines.Add(new Line
                    {
                        start = currentLineStart,
                        count = lastBreakIndex - currentLineStart + 1,
                        width = lastBreakWidth
                    });

                    currentLineStart = lastBreakIndex + 1;
                    i = lastBreakIndex;
                    currentLineCount = 0;
                    currentLineWidth = 0;
                    lastBreakIndex = -1;
                    continue;
                }
                else if (currentLineCount > 0)
                {
                    _lines.Add(new Line
                    {
                        start = currentLineStart,
                        count = currentLineCount,
                        width = currentLineWidth
                    });

                    currentLineStart = i;
                    i--;
                    currentLineCount = 0;
                    currentLineWidth = 0;
                    lastBreakIndex = -1;
                    continue;
                }
            }

            currentLineWidth += charWidth;
            currentLineCount++;

            if (char.IsWhiteSpace(c) || c == '-' || c >= 0x2E80)
            {
                lastBreakIndex = i;
                lastBreakWidth = currentLineWidth;
            }
        }

        _lines.Add(new Line
        {
            start = currentLineStart,
            count = currentLineCount,
            width = currentLineWidth
        });

        if (_fitContentMode == FitContentMode.Width && _overflowHorizontal != OverflowModeHorizontal.NextLine)
        {
            float maxW = 0;
            for (int i = 0; i < _lines.Count; i++)
                maxW = MathF.Max(maxW, _lines[i].width);
            float width = maxW * _fontSize;
            if (MathF.Abs(Size.X - width) > 0.001f)
            {
                Size = new Vector2(width, Size.Y);
            }
        }
        else if (_fitContentMode == FitContentMode.Height)
        {
            float height = _lines.Count * _fontSize * LineSpacing;
            if (Math.Abs(Size.Y - height) > 0.001f)
            {
                Size = new Vector2(Size.X, height);
            }
        }
    }

    /// <summary>
    /// Refreshes pending text writes, line breaks, and fit-content size immediately.
    /// Use when a parent needs current <see cref="ContentWidth"/> before the next UIText update.
    /// </summary>
    public void EnsureContentLayout()
    {
        if (Font == null)
        {
            return;
        }

        if (_isTmpStrWriteDirty)
        {
            SetText(_tmpStr);
        }

        if (_isLineBreakDirty)
        {
            TryRefreshTextLineBreak();
        }
    }

    /// <summary>
    /// The height of the text content based on the current line breaks, font size, and line spacing.
    /// </summary>
    public float ContentHeight => _lines.Count * _fontSize * LineSpacing;

    /// <summary>
    /// The width of the widest line in pixels, based on the current line breaks and font size.
    /// For single-line text, this is the full text width.
    /// </summary>
    public float ContentWidth
    {
        get
        {
            float maxW = 0;
            for (int i = 0; i < _lines.Count; i++)
                maxW = MathF.Max(maxW, _lines[i].width);
            return maxW * _fontSize;
        }
    }

    protected Span<char> ResizeText(int length)
    {
        _text.SetSize(length);
        _textLength = length;
        // The underlying buffer changed (e.g. UIInputBox.InsertText/DeleteText), so the cached
        // Text string is now stale and must be rebuilt on the next read.
        _isTmpStrReadDirty = true;
        return TextSpan;
    }
}