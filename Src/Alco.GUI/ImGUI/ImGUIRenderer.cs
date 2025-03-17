using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;
using ImGuiNET;

namespace Alco.GUI;

public unsafe class ImGuiRenderer : AutoDisposable
{
    private readonly RenderContext _renderContext;
    private readonly DynamicMeshRenderer _renderer;
    private readonly Material _material;
    private IntPtr _imGuiContext;

    public ImGuiRenderer(RenderingSystem renderingSystem, Material material, string name)
    {
        _renderContext = renderingSystem.CreateRenderContext(name);
        _renderer = renderingSystem.CreateDynamicMeshRenderer(_renderContext);
        _material = material;
        _imGuiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(_imGuiContext);

        ImGui.GetIO().Fonts.AddFontDefault();
        ImGui.GetIO().Fonts.Flags |= ImFontAtlasFlags.NoBakedLines;

    }

    public void Begin(GPUFrameBuffer target, float deltaTime)
    {
        ImGui.NewFrame();
        uint width = target.Width;
        uint height = target.Height;
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(width, height);
        io.DisplayFramebufferScale = new Vector2(1.0f, 1.0f);
        io.DeltaTime = deltaTime;

        _renderContext.Begin(target);
        
    }

    public void End()
    {
        ImGui.Render();
        ImDrawDataPtr drawData = ImGui.GetDrawData();

        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[i];

            ImDrawVert* vertexPtr = (ImDrawVert*)cmdList.VtxBuffer.Data;
            uint totalVertexCount = (uint)cmdList.VtxBuffer.Size;

            ushort* indexPtr = (ushort*)cmdList.IdxBuffer.Data;
            uint totalIndexCount = (uint)cmdList.IdxBuffer.Size;

            
            for(int j = 0; j < cmdList.CmdBuffer.Size; j++)
            {
                ImDrawCmdPtr cmd = cmdList.CmdBuffer[j];
                
                uint drawIndex = cmd.ElemCount;
                
            }
        }

        _renderContext.End();
    }
    

    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }

}

