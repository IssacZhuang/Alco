namespace Alco.Rendering
{
    /// <summary>
    /// The GPU buffer that represents a perspective camera with customizable field of view, aspect ratio, and clipping planes.
    /// </summary>
    public class CameraPerspectiveBuffer : BaseCameraBuffer<CameraDataPerspective>
    {
        internal CameraPerspectiveBuffer(RenderingSystem renderingSystem, string name) : base(renderingSystem, name)
        {
            _data.Transform = Transform3D.Identity;
        }

        /// <summary>
        /// Gets a reference to the camera's transform in 3D space.
        /// </summary>
        public ref Transform3D Transform
        {
            get => ref _data.Transform;
        }

        /// <summary>
        /// Gets or sets the field of view in radians.
        /// </summary>
        public float FieldOfView
        {
            get => _data.Fov;
            set
            {
                _data.Fov = value;
                _dirty = true;
            }
        }

        /// <summary>
        /// Gets or sets the aspect ratio (width/height).
        /// </summary>
        public float AspectRatio
        {
            get => _data.AspectRatio;
            set
            {
                _data.AspectRatio = value;
                _dirty = true;
            }
        }

        /// <summary>
        /// Gets or sets the near clipping plane distance.
        /// </summary>
        public float Near
        {
            get => _data.Near;
            set
            {
                _data.Near = value;
                _dirty = true;
            }
        }

        /// <summary>
        /// Gets or sets the far clipping plane distance.
        /// </summary>
        public float Far
        {
            get => _data.Far;
            set
            {
                _data.Far = value;
                _dirty = true;
            }
        }
    }
}