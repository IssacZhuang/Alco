namespace Vocore.Rendering
{
    public class CameraPerspective : BaseCamera<CameraDataPerspective>
    {
        internal CameraPerspective(RenderingSystem renderingSystem) : base(renderingSystem)
        {
        }

        public float FieldOfView
        {
            get => _data.fov;
            set
            {
                _data.fov = value;
                _dirty = true;
            }
        }

        public float AspectRatio
        {
            get => _data.aspectRatio;
            set
            {
                _data.aspectRatio = value;
                _dirty = true;
            }
        }

        public float Near
        {
            get => _data.near;
            set
            {
                _data.near = value;
                _dirty = true;
            }
        }

        public float Far
        {
            get => _data.far;
            set
            {
                _data.far = value;
                _dirty = true;
            }
        }
    }
}