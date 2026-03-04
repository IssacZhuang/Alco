using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

/// <summary>
/// A UI node that renders using a custom Material instead of a simple Texture.
/// The material's shader must use SpriteConstant layout for push constants.
/// </summary>
public class UIMaterialRenderer : UINode
{
    private Material? _material;
    private MaterialInstance? _materialInstance;
    private bool _isMaterialDirty = true;

    /// <summary>
    /// Gets or sets the material used for rendering.
    /// The material's shader must accept SpriteConstant as push constant layout.
    /// </summary>
    public Material? Material
    {
        get => _material;
        set
        {
            if (_material == value)
            {
                return;
            }
            _material = value;
            _materialInstance?.Dispose();
            _materialInstance = null;
            _isMaterialDirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the UV rectangle for sampling the texture.
    /// </summary>
    public Rect UvRect { get; set; } = Rect.One;

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);

        if (_material == null)
        {
            return;
        }

        if (_isMaterialDirty)
        {
            _materialInstance = _material.CreateInstance();
            _materialInstance.DepthStencilState = DepthStencilState.Default with
            {
                FrontFace = StencilFaceState.CompareEqual,
                BackFace = StencilFaceState.CompareEqual,
                StencilReadMask = 0xFF,
                StencilWriteMask = 0xFF,
            };
            canvas.BindCameraToMaterial(_materialInstance);
            _isMaterialDirty = false;
        }

        if (_materialInstance == null)
        {
            return;
        }

        SpriteConstant constant = new SpriteConstant
        {
            Model = RenderTransform.Matrix,
            Color = RenderColor,
            UvRect = UvRect
        };

        canvas.DrawMaterial(_materialInstance, in constant);
    }
}
