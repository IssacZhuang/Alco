using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Editor.Extensibility;

/// <summary>
/// The built-in <see cref="IPreviewPipelineFactory"/>: an HDR scene pass (Rgba16Float + D32Float)
/// followed by a Neutral <see cref="RGNode_Tonemap"/> writing into the viewport's RGBA8 output target.
/// </summary>
public sealed class DefaultPreviewPipelineFactory : IPreviewPipelineFactory
{
    private DefaultPreviewPipelineFactory()
    {
    }

    /// <summary>The shared instance.</summary>
    public static DefaultPreviewPipelineFactory Instance { get; } = new();

    /// <inheritdoc />
    public PreviewPipeline Create(PreviewPipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RenderingSystem rendering = context.Editor.RenderingSystem;

        var graph = new RenderGraph(rendering, new Extent2D((uint)context.Width, (uint)context.Height), context.Name);
        var chain = new RenderChain();
        var sceneNode = new HdrSceneNode(context, chain, context.Name + "_Scene");
        RGNode_Tonemap tonemap = context.CreateDefaultTonemap(chain);

        graph
            .Use(context.CreateRecordNode())
            .Use(sceneNode)
            .Use(tonemap);

        return new PreviewPipeline(
            graph,
            chain,
            tonemap,
            color => sceneNode.ClearColor = new Vector4(color.R, color.G, color.B, color.A));
    }

    private sealed class HdrSceneNode(PreviewPipelineContext pipelineContext, RenderChain chain, string name)
        : RenderGraphNode(name)
    {
        private RGTexture _color;
        private RGTexture _depth;

        public Vector4 ClearColor { get; set; } = new(0.15f, 0.15f, 0.15f, 1f);

        protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
        {
            _color = builder.CreateTexture(new RGTextureDesc
            {
                Name = Name + "_Color",
                Format = Format.Rgba16Float,
                Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
            });
            _depth = builder.CreateTexture(new RGTextureDesc
            {
                Name = Name + "_Depth",
                Format = Format.D32Float,
                Usage = TextureUsage.DepthStencilAttachment,
            });

            pass.Write(_color, RGUsage.ColorAttachmentWrite);
            pass.Write(_depth, RGUsage.DepthStencil);
            chain.Reset(_color);
        }

        protected override void ExecutePass(in RenderGraphContext context)
        {
            ColorAttachment colorAttachment = new()
            {
                Target = context.GetRenderTarget(_color),
                Load = LoadOp.Clear,
                Store = StoreOp.Store,
                Clear = ClearColor,
            };
            DepthAttachment depthAttachment = new()
            {
                Target = context.GetRenderTarget(_depth),
                Load = LoadOp.Clear,
                Store = StoreOp.Store,
                Clear = 1.0f,
            };
            RenderingDesc passDesc = new()
            {
                Colors = MemoryMarshal.CreateReadOnlySpan(ref colorAttachment, 1),
                Depth = depthAttachment,
            };

            Extent2D vp = context.Viewport;
            using RenderContext.RenderScope scope = context.RenderContext.BeginRenderPass(passDesc);
            scope.SetViewport(new Viewport { Width = vp.X, Height = vp.Y });
            scope.SetScissor(new Scissor { Width = vp.X, Height = vp.Y });
            pipelineContext.InvokeSceneContent(context.RenderContext, in scope);
        }
    }
}
