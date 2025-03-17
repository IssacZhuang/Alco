using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;
using ImGuiNET;

namespace Alco.GUI;

public unsafe class ImGuiRenderer : AutoDisposable
{
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly PrimitiveMesh _mesh;
    private readonly Shader _shader;
    private IntPtr _imGuiContext;

    public ImGuiRenderer(RenderingSystem renderingSystem, Shader shader, string name)
    {
        _commandBuffer = renderingSystem.GraphicsDevice.CreateCommandBuffer($"{name}_command_buffer");
        _mesh = renderingSystem.CreatePrimitiveMesh((uint)sizeof(ImDrawVert) * 64, (uint)sizeof(ushort) * 96, "ImGuiRenderer_mesh");
        _shader = shader;
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
    }

    public void End()
    {
        ImGui.Render();
        ImDrawDataPtr drawData = ImGui.GetDrawData();

        uint totalVertexBufferSize = (uint)(drawData.TotalVtxCount * sizeof(ImDrawVert));
        uint totalIndexBufferSize = (uint)(drawData.TotalIdxCount * sizeof(ushort));

        _mesh.EnsureVertexBufferSizeUnsafe(totalVertexBufferSize);
        _mesh.EnsureIndexBufferSizeUnsafe(totalIndexBufferSize);

        uint vertexBufferOffset = 0;
        uint indexBufferOffset = 0;


        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[i];

            void* vertexDataPtr = (void*)cmdList.VtxBuffer.Data;
            uint vertexDataSize = (uint)(cmdList.VtxBuffer.Size * sizeof(ImDrawVert));

            void* indexDataPtr = (void*)cmdList.IdxBuffer.Data;
            uint indexDataSize = (uint)(cmdList.IdxBuffer.Size * sizeof(ushort));

            _mesh.UpdateVertexUnsafe(vertexDataPtr, vertexDataSize, vertexBufferOffset);
            _mesh.UpdateIndicesUnsafe(indexDataPtr, indexDataSize, indexBufferOffset);

            vertexBufferOffset += vertexDataSize;
            indexBufferOffset += indexDataSize;
        }

        var io = ImGui.GetIO();
        Matrix4x4 viewProjection = Matrix4x4.CreateOrthographicOffCenter(0, io.DisplaySize.X, io.DisplaySize.Y, 0, -1, 1);
        
    }


    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }

}

