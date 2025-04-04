namespace Alco.Rendering
{
    public class CameraPerspective : BaseCamera<CameraDataPerspective>
    {
        internal CameraPerspective(RenderingSystem renderingSystem, string name) : base(renderingSystem, name)
        {
            _data.Transform = Transform3D.Identity;
        }

        public ref Transform3D Transform
        {
            get => ref _data.Transform;
        }

        public float FieldOfView
        {
            get => _data.Fov;
            set
            {
                _data.Fov = value;
                _dirty = true;
            }
        }

        public float AspectRatio
        {
            get => _data.AspectRatio;
            set
            {
                _data.AspectRatio = value;
                _dirty = true;
            }
        }

        public float Near
        {
            get => _data.Near;
            set
            {
                _data.Near = value;
                _dirty = true;
            }
        }

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