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
            var delta = GameEngine.Instance.Input.MouseDelta;
            
            rotationY -= delta.X * sensitivity / WindowSize.Y;
            rotationX += delta.Y * sensitivity / WindowSize.X;

            rotationX = math.clamp(rotationX, -maxRotationX, maxRotationX);
        }


    }
}

