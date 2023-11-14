using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Numerics;
using Vocore;

namespace Vocore.Engine
{
    public class ActorFreeLook3D
    {
        public float rotationY = 0;
        public float rotationX = 0;
        public float maxRotationX = 60;
        public float sensitivity = 0.02f;
        private bool _mouseInCenter;
        private Vector2 ScreenCenter {
            get
            {
                return WindowSize / 2;
            }
        }

        private Vector2 WindowSize
        {
            get
            {
                return GameEngine.Instance.Window.SizeF;
            }
        }

        public Quaternion Rotation
        {
            get
            {
                return Quaternion.CreateFromYawPitchRoll(math.radians(rotationY), math.radians(rotationX), 0);
            }
        }

        public void Update()
        {
            if(!_mouseInCenter)
            {
                GameEngine.Instance.Input.MousePosition = ScreenCenter;
                _mouseInCenter = true;
            }
            var delta = GameEngine.Instance.Input.MousePosition - ScreenCenter;
            GameEngine.Instance.Input.MousePosition = ScreenCenter;
            rotationY -= delta.X * sensitivity / WindowSize.Y;
            rotationX += delta.Y * sensitivity / WindowSize.X;

            rotationX = math.clamp(rotationX, -maxRotationX, maxRotationX);
        }


    }
}

