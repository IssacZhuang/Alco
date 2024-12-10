using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// A command buffer like render job.
/// </summary>
public class RenderJobDrawMesh : AutoDisposable, IRenderJob
{
    private readonly List<IMesh> _meshes;
    private readonly List<Material> _materials;
    private NativeBuffer<byte> _commands;
    private int _byteIndex;

    //cache
    private ShaderStage _pushConstantsStages;
    private int _pushConstantSize;
    private uint _indexCount;

    public GPUFrameBuffer FrameBuffer { get; set; }

    public RenderJobDrawMesh(GPUFrameBuffer frameBuffer)
    {
        _meshes = new List<IMesh>();
        _materials = new List<Material>();
        _commands = new NativeBuffer<byte>(1024);
        _byteIndex = 0;
        FrameBuffer = frameBuffer;
    }

    public unsafe void Execute(GPUCommandBuffer commandBuffer)
    {
        byte* p = _commands.UnsafePointer;
        while (*p != CommandEndCode)
        {
            byte code = *p;
            p++;
            if (code == CommandDrawWithConstantCode)
            {
                CommandDrawWithConstant command = *(CommandDrawWithConstant*)p;
                command.Execute(this, commandBuffer);
                p += sizeof(CommandDrawWithConstant);
            }
            else if (code == CommandDrawCode)
            {
                CommandDraw command = *(CommandDraw*)p;
                command.Execute(this, commandBuffer);
                p += sizeof(CommandDraw);
            }
            else if (code == CommandSetMeshCode)
            {
                CommandSetMesh command = *(CommandSetMesh*)p;
                command.Execute(this, commandBuffer);
                p += sizeof(CommandSetMesh);
            }
            else if (code == CommandSetMaterialCode)
            {
                CommandSetMaterial command = *(CommandSetMaterial*)p;
                command.Execute(this, commandBuffer, FrameBuffer);
                p += sizeof(CommandSetMaterial);
            }
            else if (code == CommandEndCode)
            {
                break;
            }
            else
            {
                throw new Exception($"Invalid command code: {code}");
            }
        }
    }

    public unsafe void Reset()
    {
        _byteIndex = 0;
        byte* p = _commands.UnsafePointer;
        *p = CommandEndCode;
        _meshes.Clear();
        _materials.Clear();
    }

    public void SetMaterial(Material material)
    {
        AddCommand(CommandSetMaterialCode, new CommandSetMaterial { MaterialIndex = _materials.IndexOf(material) });
    }

    public void SetMesh(IMesh mesh)
    {
        AddCommand(CommandSetMeshCode, new CommandSetMesh { MeshIndex = _meshes.IndexOf(mesh) });
    }

    public void Draw()
    {
        AddCommand(CommandDrawCode, new CommandDraw());
    }

    public unsafe void DrawWithConstant<T>(T constant) where T : unmanaged
    {
        CommandDrawWithConstant command = new CommandDrawWithConstant();
        byte* p = command.ConstantData;
        *(T*)p = constant;
        AddCommand(CommandDrawWithConstantCode, command);
    }

    private unsafe void AddCommand<T>(byte commandCode, T command) where T : unmanaged
    {
        int size = sizeof(T);
        if (_byteIndex + size > _commands.Length)
        {
            _commands.Resize(_commands.Length * 2);
        }
        byte* p = _commands.UnsafePointer + _byteIndex;
        *(T*)p = command;
        _byteIndex += size;
        //write end
        p = _commands.UnsafePointer + _byteIndex;
        *p = CommandEndCode;
        //the end code will be overwritten by the next command
    }

    protected override void Dispose(bool disposing)
    {
        _commands.Dispose();
    }

    private unsafe struct CommandBlock
    {
        public fixed byte Data[256];
    }

    private const byte CommandEndCode = 0;

    private const byte CommandSetMeshCode = 1;
    private struct CommandSetMesh
    {
        public int MeshIndex;

        public void Execute(RenderJobDrawMesh job, GPUCommandBuffer commandBuffer)
        {
            IMesh mesh = job._meshes[MeshIndex];
            job._indexCount = mesh.IndexCount;
            commandBuffer.SetVertexBuffer(0, mesh.VertexBuffer);
            commandBuffer.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        }
    }

    private const byte CommandSetMaterialCode = 2;
    private struct CommandSetMaterial
    {
        public int MaterialIndex;

        public void Execute(RenderJobDrawMesh job, GPUCommandBuffer commandBuffer, GPUFrameBuffer frameBuffer)
        {
            Material material = job._materials[MaterialIndex];
            ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(frameBuffer.RenderPass);
            commandBuffer.SetGraphicsPipeline(pipelineInfo.Pipeline);
            job._pushConstantsStages = pipelineInfo.PushConstantsStages;
            job._pushConstantSize = pipelineInfo.PushConstantsSize;
        }
    }

    private const byte CommandDrawCode = 3;
    private struct CommandDraw
    {
        public void Execute(RenderJobDrawMesh job, GPUCommandBuffer commandBuffer)
        {
            commandBuffer.DrawIndexed(job._indexCount, 1, 0, 0, 0);
        }
    }

    private const byte CommandDrawWithConstantCode = 4;
    private unsafe struct CommandDrawWithConstant
    {
        public fixed byte ConstantData[128];
        public void Execute(RenderJobDrawMesh job, GPUCommandBuffer commandBuffer)
        {
            fixed (byte* ptr = ConstantData)
            {
                commandBuffer.PushConstants(job._pushConstantsStages, 0, ptr, (uint)job._pushConstantSize);
                commandBuffer.DrawIndexed(job._indexCount, 1, 0, 0, 0);
            }
        }
    }

    //for unit test
    internal unsafe byte* DebugGetCommands()
    {
        return _commands.UnsafePointer;
    }
}
