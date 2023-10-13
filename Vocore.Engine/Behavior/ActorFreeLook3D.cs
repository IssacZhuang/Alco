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
        public float maxRotationX = math.radians(90);
        public float sensitivity = 0.02f;
        private bool _mouseInCenter;
        private Vector2 ScreenCenter {
            get
            {
                return new Vector2(Current.Window.Width / 2, Current.Window.Height / 2);
            }
        }

        private Vector2 WindowSize
        {
            get
            {
                return new Vector2(Current.Window.Width, Current.Window.Height);
            }
        }

        public Quaternion Rotation
        {
            get
            {
                return Quaternion.CreateFromYawPitchRoll(rotationY, rotationX, 0);
            }
        }

        public void Update()
        {
            if(!_mouseInCenter)
            {
                Input.MousePosition = ScreenCenter;
            }
            var delta = Input.MousePosition - ScreenCenter;
            Input.MousePosition = ScreenCenter;
            rotationY += delta.X * sensitivity / WindowSize.Y;
            rotationX += delta.Y * sensitivity / WindowSize.X;

            rotationX = math.clamp(rotationX, -maxRotationX, maxRotationX);
        }


    }
}

