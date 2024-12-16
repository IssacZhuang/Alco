using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// A command buffer like render job.
/// </summary>
public class RenderJobDrawMesh : AutoDisposable, IRenderJob
{
    private readonly List<IMesh> _meshes;
    private readonly List<Material> _materials;
    private readonly List<GPUResourceGroup> _resources;
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
        _resources = new List<GPUResourceGroup>();
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
            switch (code)
            {
                case CommandDrawWithConstant.Code:
                    {
                        CommandDrawWithConstant command = *(CommandDrawWithConstant*)p;
                        command.Execute(this, commandBuffer);
                        p += sizeof(CommandDrawWithConstant);
                    }
                    break;

                case CommandDraw.Code:
                    {
                        CommandDraw command = *(CommandDraw*)p;
                        command.Execute(this, commandBuffer);
                        p += sizeof(CommandDraw);
                    }
                    break;

                case CommandSetResources.Code:
                    {
                        CommandSetResources command = *(CommandSetResources*)p;
                        command.Execute(this, commandBuffer);
                        p += sizeof(CommandSetResources);
                    }
                    break;

                case CommandSetMaterial.Code:
                    {
                        CommandSetMaterial command = *(CommandSetMaterial*)p;
                        command.Execute(this, commandBuffer, FrameBuffer);
                        p += sizeof(CommandSetMaterial);
                    }
                    break;

                case CommandSetMesh.Code:
                    {
                        CommandSetMesh command = *(CommandSetMesh*)p;
                        command.Execute(this, commandBuffer);
                        p += sizeof(CommandSetMesh);
                    }
                    break;

                case CommandEndCode:
                    return;

                default:
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
        _resources.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetMaterial(Material material)
    {
        int index = _materials.Count;
        _materials.Add(material);
        AddCommand(CommandSetMaterial.Code, new CommandSetMaterial { MaterialIndex = index });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetMesh(IMesh mesh)
    {
        int index = _meshes.Count;
        _meshes.Add(mesh);
        AddCommand(CommandSetMesh.Code, new CommandSetMesh { MeshIndex = index });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw()
    {
        AddCommand(CommandDraw.Code, new CommandDraw());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResources(uint slot, GPUResourceGroup resources)
    {
        int index = _resources.Count;
        _resources.Add(resources);
        AddCommand(CommandSetResources.Code, new CommandSetResources { Slot = slot, ResourcesIndex = index });
    }

    public unsafe void DrawWithConstant<T>(T constant) where T : unmanaged
    {
        CommandDrawWithConstant command = new CommandDrawWithConstant();
        byte* p = command.ConstantData;
        *(T*)p = constant;
        AddCommand(CommandDrawWithConstant.Code, command);
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

    private struct CommandSetMesh
    {
        public const byte Code = 1;
        public int MeshIndex;

        public void Execute(RenderJobDrawMesh job, GPUCommandBuffer commandBuffer)
        {
            IMesh mesh = job._meshes[MeshIndex];
            job._indexCount = mesh.IndexCount;
            commandBuffer.SetVertexBuffer(0, mesh.VertexBuffer);
            commandBuffer.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        }
    }

    private struct CommandSetMaterial
    {
        public const byte Code = 2;
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

    private struct CommandSetResources
    {
        public const byte Code = 3;
        public uint Slot;
        public int ResourcesIndex;
        public void Execute(RenderJobDrawMesh job, GPUCommandBuffer commandBuffer)
        {
            GPUResourceGroup resources = job._resources[ResourcesIndex];
            commandBuffer.SetGraphicsResources(Slot, resources);
        }

    }


    private struct CommandDraw
    {
        public const byte Code = 4;
        public void Execute(RenderJobDrawMesh job, GPUCommandBuffer commandBuffer)
        {
            commandBuffer.DrawIndexed(job._indexCount, 1, 0, 0, 0);
        }
    }

    private unsafe struct CommandDrawWithConstant
    {
        public const byte Code = 5;
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
