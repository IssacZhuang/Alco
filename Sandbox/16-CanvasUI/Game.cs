using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.GUI;
using System.Diagnostics;
using Alco.Graphics;
using Alco.ImGUI;

public class Game : GameEngine
{
    private enum Display{
        Button,
        Sprite,
        Text,
        InputBox,
        LayoutVertical,
        LayoutHorizontal,
        LayoutGrid,
        Slider,
        List,
        VirtualList,
        VirtualGridList
    }

    private readonly Canvas _canvas;
    private readonly Font _font;

    private CanvasUIFactory _factory;

    private UINode _root;
    private UIInputBox _inputBox;
    private UISlider _slider;
    private UILayout _verticalLayout;
    private UILayout _horizontalLayout;
    private UILayout _gridLayout;
    private UIMask _verticalLayoutMask;
    private UIMask _horizontalLayoutMask;
    private UIMask _gridLayoutMask;

    private UIImage _sprite;
    private IntList _intList;
    private IntVirtualList _intVirtualList;
    private IntVirtualList _intVirtualGridList;
    private UISlider _listSlider;
    private UISlider _virtualListSlider;
    private UISlider _virtualGridListSlider;
    private UIButton _button1;
    private UIText _label;

    private Display _display = Display.Button;


    private float _alignHorizontal = TextAlign.Left;
    private float _alignVertical = TextAlign.Top;
    private float _lineSpacing = 1f;
    private float _fontSize = 16;
    private float _progress = 0f;
    private float _pivotY = 0f;
    private float _angle = 0f;
    private int _itemCount = 0;
    private int _listCount = 30;
    private int _virtualListCount = 1000000;
    private float _spacingX = 5f;
    private float _spacingY = 5f;
    private UINode? _selectedNode;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _font = BuiltInAssets.Font_Default;

        CavanUIFactoryStyle style = new CavanUIFactoryStyle
        {
            Font = _font,
            FontSize = 16,
            TextColor = 0xffffff,
            SliderSize = new Vector2(140, 24),
            SliderHandleSize = new Vector2(16, 24),
            SliderColor = 0x2a2a2a,
            SliderHandleColor = 0x373737,
            SliderHandleHoverColor = 0x525252,
            SliderHandleDragColor = 0x234A6C,
            DefaultButtonText = "Button",
            ButtonSize = new Vector2(80, 24),
            ButtonTextColor = 0xffffff,
            ButtonColor = 0x2a2a2a,
            ButtonPressedColor = 0x234A6C,
            ButtonHoverColor = 0x3a3a3a,
            CheckBoxColor = 0x2a2a2a,
            CheckBoxHoverColor = 0x525252,
            CheckBoxPressedColor = 0x373737,
        };

        _factory = new CanvasUIFactory(style);

        UIInputTracker inputTracker = new UIInputTracker(Input, MainView);

        Material defaultSpriteMaterial = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_Sprite);
        defaultSpriteMaterial.BlendState = BlendState.NonPremultipliedAlpha;
        Material defaultTextMaterial = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_Text);
        defaultTextMaterial.BlendState = BlendState.NonPremultipliedAlpha;


        _canvas = RenderingSystem.CreateCanvas(inputTracker, defaultSpriteMaterial, defaultTextMaterial, _font);
        _canvas.Size = new Vector2(setting.View.Width, setting.View.Height);
        _canvas.DebugDrawColor = new Vector4(0, 1, 0, 1);

        _root = _canvas.Root;


        UIInputBox inputBox = new UIInputBox()
        {
            Font = _font,
            Position = Vector2.Zero,
            Size = new Vector2(100, 100),
            Color = 0xffffff,
            AlignHorizontal = TextAlign.Left,
            AlignVertical = TextAlign.Top,
            OverflowHorizontal = OverflowModeHorizontal.NextLine,
            OverflowVertical = OverflowModeVertical.Clamp,
            Text = "This is an editable text",
        };


        _inputBox = inputBox;

        // _root.Add(bg);
        _root.Add(inputBox);

        _label = new UIText()
        {
            Font = _font,
            Position = Vector2.Zero,
            Size = new Vector2(100, 100),
            Color = 0xffffff,
            AlignHorizontal = TextAlign.Left,
            AlignVertical = TextAlign.Top,
            OverflowHorizontal = OverflowModeHorizontal.NextLine,
            OverflowVertical = OverflowModeVertical.Clamp,
            Text = "A UI label",
        };

        _root.Add(_label);

        // Create Vertical Layout Demo
        _verticalLayout = new UILayout(LayoutType.Vertical)
        {
            Position = Vector2.Zero,
            Size = new Vector2(200, 100),
            Padding = new Padding(0, 8, 0, 8),
            Spacing = new Vector2(4f),
            FitContentSize = true,
        };
        
        _verticalLayoutMask = CreateLayoutDemo(_verticalLayout, "Vertical Layout", 0xaaaaaa);

        // Create Horizontal Layout Demo
        _horizontalLayout = new UILayout(LayoutType.Horizontal)
        {
            Position = Vector2.Zero,
            Size = new Vector2(100, 200),
            Padding = new Padding(8, 0, 8, 0),
            Spacing = new Vector2(4f),
            FitContentSize = true,
        };
        
        _horizontalLayoutMask = CreateLayoutDemo(_horizontalLayout, "Horizontal Layout", 0xbbaaaa);

        // Create Grid Layout Demo  
        _gridLayout = new UILayout(LayoutType.Grid)
        {
            Position = Vector2.Zero,
            Size = new Vector2(300, 200),
            Spacing = new Vector2(4f),
            Padding = new Padding(10, 10, 10, 10),
        };
        
        _gridLayoutMask = CreateLayoutDemo(_gridLayout, "Grid Layout", 0xaabbaa);

        _root.Add(_verticalLayoutMask);
        _root.Add(_horizontalLayoutMask);  
        _root.Add(_gridLayoutMask);
        

        UISlider slider = _factory.CreateSlider();
        slider.Position = Vector2.Zero;
        _slider = slider;
        _root.Add(slider);

        // simple demo button centered
        _button1 = _factory.CreateButton("Button 1");
        _button1.Position = Vector2.Zero;
        _button1.Size = new Vector2(140, 140);
        _root.Add(_button1);

        UIButton button2 = _factory.CreateButton("Button 2");
        button2.Position = Vector2.Zero;
        button2.Size = new Vector2(120, 120);
        _button1.Add(button2);

        UIButton button3 = _factory.CreateButton("Button 3");
        button3.Position = Vector2.Zero;
        button3.Size = new Vector2(100, 100);
        button2.Add(button3);


        Texture2D texSelection = AssetSystem.Load<Texture2D>("Selection.png");
        _sprite = new UIImage()
        {
            Texture = texSelection,
            Size = new Vector2(100, 100),
            Position = Vector2.Zero,
            ImageType = ImageType.Sliced,
        };

        _root.Add(_sprite);

        // Int list demo
        _intList = new IntList()
        {
            Position = Vector2.Zero,
            Size = new Vector2(120, 200),
        };
        _intList.ItemFont = _font;
        _root.Add(_intList);

        PopulateIntList(_listCount);

        // Virtual Int list demo (1 million items)
        _intVirtualList = new IntVirtualList()
        {
            Position = Vector2.Zero,
            Size = new Vector2(120, 200),
        };
        _intVirtualList.ItemFont = _font;
        _root.Add(_intVirtualList);

        PopulateIntVirtualList(_virtualListCount);

        // Virtual Int grid list demo (3 items per row)
        _intVirtualGridList = new IntVirtualList()
        {
            Position = Vector2.Zero,
            Size = new Vector2(360, 200),
        };
        _intVirtualGridList.ItemFont = _font;
        _intVirtualGridList.ColumnsPerRow = 3;
        _intVirtualGridList.Spacing = new Vector2(5f, 4f);
        _root.Add(_intVirtualGridList);

        PopulateIntVirtualGridList(_virtualListCount);

        // Create and link vertical sliders for lists
        _listSlider = _factory.CreateSlider();
        _listSlider.Rotation = new Rotation2D(90);
        _listSlider.Position = new Vector2(_intList.Size.X * 0.5f + 20f, 0f);
        _root.Add(_listSlider);
        _intList.Scrollable.SliderVertical = _listSlider;

        _virtualListSlider = _factory.CreateSlider();
        _virtualListSlider.Rotation = new Rotation2D(90);
        _virtualListSlider.Position = new Vector2(_intVirtualList.Size.X * 0.5f + 20f, 0f);
        _root.Add(_virtualListSlider);
        _intVirtualList.Scrollable.SliderVertical = _virtualListSlider;

        _virtualGridListSlider = _factory.CreateSlider();
        _virtualGridListSlider.Rotation = new Rotation2D(90);
        _virtualGridListSlider.Position = new Vector2(_intVirtualGridList.Size.X * 0.5f + 20f, 0f);
        _root.Add(_virtualGridListSlider);
        _intVirtualGridList.Scrollable.SliderVertical = _virtualGridListSlider;

        // default display
        UpdateDisplayActive();
    }

    private UIMask CreateLayoutDemo(UILayout layout, string title, uint backgroundColor)
    {
        // Create background sprite
        UIImage bgLayout = new UIImage()
        {
            Position = new Vector2(200, 100),
            Size = new Vector2(layout.Size.X + 20, layout.Size.Y + 40),
            Color = backgroundColor,
            Anchor = Anchor.Stretch,
        };

        // Create title label
        UIText titleLabel = new UIText()
        {
            Font = _font,
            Position = new Vector2(0, layout.Size.Y * 0.5f + 15),
            Size = new Vector2(layout.Size.X, 20),
            Color = 0xffffff,
            FontSize = 14,
            AlignHorizontal = TextAlign.Center,
            AlignVertical = TextAlign.Center,
            Text = title,
        };

        UIMask mask = new UIMask()
        {
            Position = Vector2.Zero,
            Size = new Vector2(layout.Size.X + 20, layout.Size.Y + 40)
        };

        UIScrollable scrollable = new UIScrollable()
        {
            Position = Vector2.Zero,
            Size = new Vector2(layout.Size.X + 20, layout.Size.Y + 40),
            ScrollMode = SrollMode.Vertical | SrollMode.Horizontal,
        };

        scrollable.Add(bgLayout);
        scrollable.Add(titleLabel);
        scrollable.Add(layout);
        scrollable.Content = layout;

        mask.Add(scrollable);
        
        return mask;
    }

    protected override void OnTick(float delta)
    {
        //just move this to OnUpdate if u want to update UI logic every frame
        _canvas.Tick(delta);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        DebugStats.Text(FrameRate);

        //_canvas.Tick(_root, delta);
        _canvas.Update(MainFrameBuffer, delta);

        // ImGUI Controls
        ImGui.Begin("Canvas UI Controls");

        // Display frame rate
        FixedString32 framerateText = new FixedString32();
        framerateText.Append("Frame Rate: ");
        framerateText.Append(FrameRate);
        ImGui.Text(framerateText);

        // Display selector (enum-aware)
        if (ImGui.Combo("Display", ref _display))
        {
            UpdateDisplayActive();
        }

        // Parameters per display
        switch (_display)
        {
            case Display.Button:
                // No extra params for now
                break;

            case Display.Sprite:
            {
                float width = _sprite.Size.X;
                if (ImGui.SliderFloat("Sprite width", ref width, 0, 512))
                {
                    _sprite.Size = new Vector2(width, _sprite.Size.Y);
                }
                float height = _sprite.Size.Y;
                if (ImGui.SliderFloat("Sprite height", ref height, 0, 512))
                {
                    _sprite.Size = new Vector2(_sprite.Size.X, height);
                }
                break;
            }

            case Display.Text:
            {
                if (ImGui.SliderFloat("Align Horizontal", ref _alignHorizontal, -0.5f, 0.5f))
                {
                    _label.AlignHorizontal = _alignHorizontal;
                }
                if (ImGui.SliderFloat("Align Vertical", ref _alignVertical, -0.5f, 0.5f))
                {
                    _label.AlignVertical = _alignVertical;
                }
                if (ImGui.SliderFloat("Font Size", ref _fontSize, 8, 32))
                {
                    _label.FontSize = _fontSize;
                }
                if (ImGui.SliderFloat("Line Spacing", ref _lineSpacing, 0.5f, 2f))
                {
                    _label.LineSpacing = _lineSpacing;
                }
                if (ImGui.SliderFloat("Angle", ref _angle, 0, 360))
                {
                    _label.Rotation = new Rotation2D(_angle);
                }
                break;
            }

            case Display.InputBox:
            {
                if (ImGui.SliderFloat("Align Horizontal", ref _alignHorizontal, -0.5f, 0.5f))
                {
                    _inputBox.AlignHorizontal = _alignHorizontal;
                }
                if (ImGui.SliderFloat("Align Vertical", ref _alignVertical, -0.5f, 0.5f))
                {
                    _inputBox.AlignVertical = _alignVertical;
                }
                if (ImGui.SliderFloat("Font Size", ref _fontSize, 8, 32))
                {
                    _inputBox.FontSize = _fontSize;
                }
                if (ImGui.SliderFloat("Line Spacing", ref _lineSpacing, 0.5f, 2f))
                {
                    _inputBox.LineSpacing = _lineSpacing;
                }
                if (ImGui.SliderFloat("Angle", ref _angle, 0, 360))
                {
                    _inputBox.Rotation = new Rotation2D(_angle);
                }
                break;
            }

            case Display.LayoutVertical:
            {
                if (ImGui.SliderFloat("Pivot Y", ref _pivotY, -0.5f, 0.5f))
                {
                    _verticalLayout.Pivot = new Vector2(0f, _pivotY);
                }

                    // Padding controls
                    float vPadTop = _verticalLayout.Padding.Top;
                    if (ImGui.SliderFloat("Padding Top", ref vPadTop, 0, 50))
                    {
                        var p = _verticalLayout.Padding;
                        p.Top = vPadTop;
                        _verticalLayout.Padding = p;
                        _verticalLayout.UpdateLayout();
                    }
                    float vPadBottom = _verticalLayout.Padding.Bottom;
                    if (ImGui.SliderFloat("Padding Bottom", ref vPadBottom, 0, 50))
                    {
                        var p = _verticalLayout.Padding;
                        p.Bottom = vPadBottom;
                        _verticalLayout.Padding = p;
                        _verticalLayout.UpdateLayout();
                    }

                    // Spacing controls
                    float vSpacingY = _verticalLayout.Spacing.Y;
                    if (ImGui.SliderFloat("Spacing Y", ref vSpacingY, 0, 20))
                    {
                        _verticalLayout.Spacing = new Vector2(_verticalLayout.Spacing.X, vSpacingY);
                        _verticalLayout.UpdateLayout();
                    }
                float itemCountFloat = _itemCount;
                if (ImGui.SliderFloat("Item Count", ref itemCountFloat, 0, 10))
                {
                    _itemCount = (int)itemCountFloat;
                    PopulateLayoutButtons(_verticalLayout);
                }
                break;
            }

            case Display.LayoutHorizontal:
            {
                if (ImGui.SliderFloat("Pivot X", ref _pivotY, -0.5f, 0.5f))
                {
                    _horizontalLayout.Pivot = new Vector2(_pivotY, 0f);
                }

                    // Padding controls
                    float hPadLeft = _horizontalLayout.Padding.Left;
                    if (ImGui.SliderFloat("Padding Left", ref hPadLeft, 0, 50))
                    {
                        var p = _horizontalLayout.Padding;
                        p.Left = hPadLeft;
                        _horizontalLayout.Padding = p;
                        _horizontalLayout.UpdateLayout();
                    }
                    float hPadRight = _horizontalLayout.Padding.Right;
                    if (ImGui.SliderFloat("Padding Right", ref hPadRight, 0, 50))
                    {
                        var p = _horizontalLayout.Padding;
                        p.Right = hPadRight;
                        _horizontalLayout.Padding = p;
                        _horizontalLayout.UpdateLayout();
                    }

                    // Spacing controls
                    float hSpacingX = _horizontalLayout.Spacing.X;
                    if (ImGui.SliderFloat("Spacing X", ref hSpacingX, 0, 20))
                    {
                        _horizontalLayout.Spacing = new Vector2(hSpacingX, _horizontalLayout.Spacing.Y);
                        _horizontalLayout.UpdateLayout();
                    }
                float itemCountFloat = _itemCount;
                if (ImGui.SliderFloat("Item Count", ref itemCountFloat, 0, 10))
                {
                    _itemCount = (int)itemCountFloat;
                    PopulateLayoutButtons(_horizontalLayout);
                }
                break;
            }

            case Display.LayoutGrid:
            {
                if (ImGui.SliderFloat("Spacing X", ref _spacingX, 0, 20))
                {
                    _gridLayout.Spacing = new Vector2(_spacingX, _spacingY);
                    _gridLayout.UpdateLayout();
                }
                
                if (ImGui.SliderFloat("Spacing Y", ref _spacingY, 0, 20))
                {
                    _gridLayout.Spacing = new Vector2(_spacingX, _spacingY);
                    _gridLayout.UpdateLayout();
                }

                    // Padding controls
                    float gPadTop = _gridLayout.Padding.Top;
                    if (ImGui.SliderFloat("Padding Top", ref gPadTop, 0, 50))
                    {
                        var p = _gridLayout.Padding;
                        p.Top = gPadTop;
                        _gridLayout.Padding = p;
                        _gridLayout.UpdateLayout();
                    }
                    float gPadBottom = _gridLayout.Padding.Bottom;
                    if (ImGui.SliderFloat("Padding Bottom", ref gPadBottom, 0, 50))
                    {
                        var p = _gridLayout.Padding;
                        p.Bottom = gPadBottom;
                        _gridLayout.Padding = p;
                        _gridLayout.UpdateLayout();
                    }
                    float gPadLeft = _gridLayout.Padding.Left;
                    if (ImGui.SliderFloat("Padding Left", ref gPadLeft, 0, 50))
                    {
                        var p = _gridLayout.Padding;
                        p.Left = gPadLeft;
                        _gridLayout.Padding = p;
                        _gridLayout.UpdateLayout();
                    }
                    float gPadRight = _gridLayout.Padding.Right;
                    if (ImGui.SliderFloat("Padding Right", ref gPadRight, 0, 50))
                    {
                        var p = _gridLayout.Padding;
                        p.Right = gPadRight;
                        _gridLayout.Padding = p;
                        _gridLayout.UpdateLayout();
                    }
                
                float itemCountFloat = _itemCount;
                if (ImGui.SliderFloat("Item Count", ref itemCountFloat, 0, 20))
                {
                    _itemCount = (int)itemCountFloat;
                    PopulateLayoutButtons(_gridLayout);
                }
                
                // Show calculated columns info
                ImGui.Text($"Auto-calculated columns per row based on item size and spacing");
                break;
            }

            case Display.Slider:
            {
                if (ImGui.SliderFloat("Progress", ref _progress, 0, 1))
                {
                    _slider.Value = _progress;
                }
                if (ImGui.SliderFloat("Angle", ref _angle, 0, 360))
                {
                    _slider.Rotation = new Rotation2D(_angle);
                }
                break;
            }

            case Display.List:
            {
                float count = _listCount;
                if (ImGui.SliderFloat("List Count", ref count, 0, 100))
                {
                    _listCount = (int)count;
                    PopulateIntList(_listCount);
                }
                break;
            }

            case Display.VirtualList:
            {
                float count = _virtualListCount;
                    if (ImGui.SliderFloat("Virtual List Count", ref count, 1, 10000000))
                {
                    _virtualListCount = (int)count;
                    PopulateIntVirtualList(_virtualListCount);
                }
                
                // Display performance info
                ImGui.Text($"Rendering {_virtualListCount:N0} items efficiently!");
                ImGui.Text("Only visible items are actually created and rendered.");
                ImGui.Text("Try scrolling through the list smoothly.");
                
                break;
            }
            
            case Display.VirtualGridList:
            {
                float count = _virtualListCount;
                    if (ImGui.SliderFloat("Virtual Grid Count", ref count, 1, 10000000))
                {
                    _virtualListCount = (int)count;
                    PopulateIntVirtualGridList(_virtualListCount);
                }
                ImGui.Text("Grid layout with 3 items per row.");
                break;
            }
        }

        ImGui.End();

        // UI Tree Inspector Window
        ImGui.Begin("UI Tree Inspector");
        _root.DrawDebugTreeWithInspector(ref _selectedNode);
        ImGui.End();
    }

    private void PopulateIntList(int count)
    {
        // generate sequential integers 0..count-1
        List<int> data = new List<int>(count);
        for (int i = 0; i < count; i++) data.Add(i);
        _intList.SetItems(data);
    }

    private void PopulateIntVirtualList(int count)
    {
        // Generate sequential integers 0..count-1 efficiently for virtual list
        List<int> data = new List<int>(count);
        for (int i = 0; i < count; i++) data.Add(i);
        _intVirtualList.SetItems(data);
    }

    private void PopulateIntVirtualGridList(int count)
    {
        // Generate sequential integers 0..count-1 efficiently for virtual grid list
        List<int> data = new List<int>(count);
        for (int i = 0; i < count; i++) data.Add(i);
        _intVirtualGridList.SetItems(data);
    }

    private void PopulateLayoutButtons(UILayout layout)
    {
        layout.RemoveAllChildren();
        for (int i = 0; i < _itemCount; i++)
        {
            int index = i;
            UIButton btn = _factory.CreateButton("Btn " + i);
            btn.EventOnClick += (canvas, mousePosition) =>
            {
                Log.Info("Button " + index + " clicked");
            };
            
            // Adjust button size based on layout type
            if (layout.LayoutType == LayoutType.Grid)
            {
                btn.Size = new Vector2(60, 30);
            }
            else if (layout.LayoutType == LayoutType.Horizontal)
            {
                btn.Size = new Vector2(50, 30);
            }
            
            layout.Add(btn, false);
        }
        layout.UpdateLayout();
    }

    private async void TestAsync()
    {
        await Task.Delay(1000);
        Log.Info("TestAsync 1");
        await Task.Delay(1000);
        Log.Info("TestAsync 2");
        await Task.Delay(1000);
        Log.Info("TestAsync 3");
    }

    protected override void OnStop()
    {
        _canvas.Dispose();
    }

    private void UpdateDisplayActive()
    {
        // disable all
        _button1.IsEnable = false;
        _sprite.IsEnable = false;
        _inputBox.IsEnable = false;
        _verticalLayoutMask.IsEnable = false;
        _horizontalLayoutMask.IsEnable = false;
        _gridLayoutMask.IsEnable = false;
        _slider.IsEnable = false;
        _intList.IsEnable = false;
        _intVirtualList.IsEnable = false;
        _intVirtualGridList.IsEnable = false;
        _label.IsEnable = false;
        if (_listSlider != null) _listSlider.IsEnable = false;
        if (_virtualListSlider != null) _virtualListSlider.IsEnable = false;
        if (_virtualGridListSlider != null) _virtualGridListSlider.IsEnable = false;
        
        switch (_display)
        {
            case Display.Button:
                _button1.IsEnable = true;
                break;
            case Display.Sprite:
                _sprite.IsEnable = true;
                break;
            case Display.Text:
                _label.IsEnable = true;
                break;
            case Display.InputBox:
                _inputBox.IsEnable = true;
                break;
            case Display.LayoutVertical:
                _verticalLayoutMask.IsEnable = true;
                // Initialize with some buttons if empty
                if (_verticalLayout.Children.Count == 0)
                {
                    _itemCount = 3;
                    PopulateLayoutButtons(_verticalLayout);
                }
                break;
            case Display.LayoutHorizontal:
                _horizontalLayoutMask.IsEnable = true;
                // Initialize with some buttons if empty
                if (_horizontalLayout.Children.Count == 0)
                {
                    _itemCount = 3;
                    PopulateLayoutButtons(_horizontalLayout);
                }
                break;
            case Display.LayoutGrid:
                _gridLayoutMask.IsEnable = true;
                // Initialize with some buttons if empty
                if (_gridLayout.Children.Count == 0)
                {
                    _itemCount = 6;
                    PopulateLayoutButtons(_gridLayout);
                }
                break;
            case Display.Slider:
                _slider.IsEnable = true;
                break;
            case Display.List:
                _intList.IsEnable = true;
                if (_listSlider != null) _listSlider.IsEnable = true;
                break;
            case Display.VirtualList:
                _intVirtualList.IsEnable = true;
                if (_virtualListSlider != null) _virtualListSlider.IsEnable = true;
                break;
            case Display.VirtualGridList:
                _intVirtualGridList.IsEnable = true;
                if (_virtualGridListSlider != null) _virtualGridListSlider.IsEnable = true;
                break;
        }
    }
}