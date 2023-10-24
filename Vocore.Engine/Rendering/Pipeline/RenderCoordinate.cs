using System;
using System.Numerics;
using Veldrid;
using Vocore.Unsafe;

#pragma warning disable CS8618

namespace Vocore.Engine
{
    public class RenderCoordinate : BaseRenderPipeline
    {
        public override int Order => 2000;
        private struct Vertex
        {
            public Vector3 Position;
            public Vector4 Color;

            public Vertex(Vector3 position, Vector4 color)
            {
                Position = position;
                Color = color;
            }
        }
        private const float CordLength = 0.1f;
        //x,y,z cordinate
        private static readonly Vertex[] Vertices = new Vertex[] {
            //x
            new Vertex(new Vector3(0, 0, 0), new Vector4(1, 0.3f, 0.3f, 1)),
            new Vertex(new Vector3(CordLength, 0, 0), new Vector4(1, 0.3f, 0.3f, 1)),
            //inverse x
            new Vertex(new Vector3(0, 0, 0), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(-CordLength, 0, 0), new Vector4(1, 1, 1, 1)),
            //y
            new Vertex(new Vector3(0, 0, 0), new Vector4(0.3f, 1, 0.3f, 1)),
            new Vertex(new Vector3(0, CordLength, 0), new Vector4(0.3f, 1, 0.3f, 1)),
            //inverse y
            new Vertex(new Vector3(0, 0, 0), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(0, -CordLength, 0), new Vector4(1, 1, 1, 1)),
            //z
            new Vertex(new Vector3(0, 0, 0), new Vector4(0.3f, 0.3f, 1, 1)),
            new Vertex(new Vector3(0, 0, CordLength), new Vector4(0.3f, 0.3f, 1, 1)),
            //inverse z
            new Vertex(new Vector3(0, 0, 0), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(0, 0, -CordLength), new Vector4(1, 1, 1, 1)),
        };
        private static readonly ushort[] Indices = new ushort[] {
            0, 1,
            2, 3,
            4, 5,
            6, 7,
            8, 9,
            10, 11
        };


        private static readonly uint VertexSizeInBytes = (uint)UtilsMemory.SizeOf<Vertex>();
        private static readonly IndexFormat IndexFormat = IndexFormat.UInt16;
        private Shader _shader;
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;
        private DeviceBuffer _matrixBuffer;
        private ResourceSet _matrixResourceSet;
        private CameraOrthographic _camera;

        public Matrix4x4 Matrix
        {
            get
            {
                ICamera? currentCamera = Current.Camera;
                if (currentCamera == null)
                {
                    return Matrix4x4.Identity;
                }

                return Matrix4x4.CreateFromQuaternion(currentCamera.Rotation) * Matrix4x4.CreateTranslation(-0.6f, 0.3f, -1) * _camera.ProjectionMatrix;
            }
        }

        public override void OnCreate(GraphicsDevice device)
        {
            base.OnCreate(device);
            _camera = new CameraOrthographic();
            _device = device;
            _shader = ShaderPool.Get("Line.glsl");
            _vertexBuffer = _factory.CreateBuffer(new BufferDescription((uint)Vertices.Length * VertexSizeInBytes, BufferUsage.VertexBuffer));
            _indexBuffer = _factory.CreateBuffer(new BufferDescription((uint)Indices.Length * sizeof(ushort), BufferUsage.IndexBuffer));
            _matrixBuffer = _factory.CreateBuffer(new BufferDescription((uint)UtilsMemory.SizeOf<Matrix4x4>(), BufferUsage.UniformBuffer));
            _matrixResourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
                _factory.CreateResourceLayout(new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ModelViewProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex))),
                _matrixBuffer));

            _device.UpdateBuffer(_vertexBuffer, 0, Vertices);
            _device.UpdateBuffer(_indexBuffer, 0, Indices);

        }

        public override void OnDraw(CommandList commandList)
        {
            //_device.UpdateBuffer(_matrixBuffer, 0, Matrix);
            commandList.Begin();
            commandList.UpdateBuffer(_matrixBuffer, 0, Matrix);
            commandList.SetPipeline(_shader.Pipeline);
            commandList.SetFramebuffer(_device.SwapchainFramebuffer);
            commandList.SetVertexBuffer(0, _vertexBuffer);
            commandList.SetIndexBuffer(_indexBuffer, IndexFormat);
            commandList.SetGraphicsResourceSet(0, _matrixResourceSet);
            commandList.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);
            commandList.End();
            _device.SubmitCommands(commandList);
        }

        public override void OnDestroy()
        {
            _shader.Dispose();
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
            _matrixBuffer.Dispose();
            _matrixResourceSet.Dispose();
        }
    }
}

