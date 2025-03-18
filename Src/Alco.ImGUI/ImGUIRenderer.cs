using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;
using ImGuiNET;

namespace Alco.ImGUI;

public unsafe class ImGUIRenderer : AutoDisposable
{
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly PrimitiveMesh _mesh;
    private readonly GraphicsValueBuffer<Matrix4x4> _viewProjectionBuffer;
    private readonly Material _material;
    private IntPtr _imGuiContext;
    private GPUFrameBuffer? _target;
    private readonly uint _shaderId_Texture;
    private readonly IntPtr _fontTextureId = (IntPtr)1;
    private Texture2D _fontTexture;

    public ImGUIRenderer(RenderingSystem renderingSystem, Material material, string name)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _commandBuffer = _device.CreateCommandBuffer($"{name}_command_buffer");
        _mesh = renderingSystem.CreatePrimitiveMesh((uint)sizeof(ImDrawVert) * 64, (uint)sizeof(ushort) * 96, "ImGuiRenderer_mesh");
        _material = material;
        _viewProjectionBuffer = renderingSystem.CreateGraphicsValueBuffer<Matrix4x4>("ImGuiRenderer_view_projection_buffer");
        _imGuiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(_imGuiContext);

        _shaderId_Texture = _material.GetResourceId(ShaderResourceId.Texture);

        ImGuiIOPtr io = ImGui.GetIO();  
        io.Fonts.AddFontDefault();
        io.Fonts.Flags |= ImFontAtlasFlags.NoBakedLines;

        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out int bytesPerPixel);
        io.Fonts.SetTexID(_fontTextureId);

        ReadOnlySpan<byte> fontTextureData = new ReadOnlySpan<byte>(pixels, width * height * bytesPerPixel);
        _fontTexture = renderingSystem.CreateTexture2D(fontTextureData, (uint)width, (uint)height, ImageLoadOption.Default);

    }

    public void Begin(GPUFrameBuffer target, float deltaTime)
    {
        uint width = target.Width;
        uint height = target.Height;
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(width, height);
        io.DisplayFramebufferScale = new Vector2(1.0f, 1.0f);
        io.DeltaTime = deltaTime;

        ImGui.NewFrame();
        _target = target;
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
        _viewProjectionBuffer.Value = Matrix4x4.CreateOrthographicOffCenter(0, io.DisplaySize.X, io.DisplaySize.Y, 0, -1, 1);
        _viewProjectionBuffer.UpdateBuffer();

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        ShaderPipelineInfo pipelineInfo = _material.GetPipelineInfo(_target!.RenderPass);

        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(_target);
        _commandBuffer.SetGraphicsPipeline(pipelineInfo.Pipeline);
        _commandBuffer.SetVertexBuffer(0, _mesh.VertexBuffer);
        _commandBuffer.SetIndexBuffer(_mesh.IndexBuffer, IndexFormat.UInt16);
        _material.PushResourceToCommandBuffer(_commandBuffer);

        uint vertexOffset = 0;
        uint indexOffset = 0;

        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[i];

            for (int j = 0; j < cmdList.CmdBuffer.Size; j++)
            {
                ImDrawCmdPtr cmd = cmdList.CmdBuffer[j];
                nint textureId = cmd.TextureId;

                if (textureId == _fontTextureId)
                {
                    _commandBuffer.SetGraphicsResources(_shaderId_Texture, _fontTexture.EntrySample);
                }
                else
                {
                    //todo: get texture by native handle
                    _commandBuffer.SetGraphicsResources(_shaderId_Texture, _renderingSystem.TextureWhite.EntrySample);
                }
                Vector4 uvRect = new Vector4(0, 0, 1, 1);
                _commandBuffer.PushConstants(pipelineInfo.PushConstantsStages, uvRect);

                Vector4 clipRect = cmd.ClipRect;

                _commandBuffer.SetScissorRect(
                    (uint)clipRect.X,
                    (uint)clipRect.Y,
                    (uint)(clipRect.Z - clipRect.X),
                    (uint)(clipRect.W - clipRect.Y));


                _commandBuffer.DrawIndexed(cmd.ElemCount, 1, cmd.IdxOffset + indexOffset, (int)(cmd.VtxOffset + vertexOffset), 0);
            }

            indexOffset += (uint)cmdList.IdxBuffer.Size;
            vertexOffset += (uint)cmdList.VtxBuffer.Size;
        }


        _commandBuffer.End();
        _device.Submit(_commandBuffer);
        _target = null;
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _commandBuffer.Dispose();
            _viewProjectionBuffer.Dispose();
            _mesh.Dispose();
        }
        ImGui.DestroyContext(_imGuiContext);
    }

}

