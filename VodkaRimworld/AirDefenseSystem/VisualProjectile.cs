using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace ADS
{
    public class VisualProjectile
    {
        private int _lifeTicks = 60;

        public Vector3 position;
        public Quaternion rotation;
        public Vector3 size;

        public Vector3 speed;

        public int renderId;

        public int LifeTick => _lifeTicks;
        public bool IsAlive => _lifeTicks > 0;

        public VisualProjectile(Vector3 pos, Vector3 spd, Vector2 size2D)
        {
            position = pos;
            speed = spd;
            speed.y = 0;
            rotation = Quaternion.AngleAxis(speed.AngleFlat(), Vector3.up);
            size = new Vector3(size2D.x, 1f, size2D.y);
        }

        public void Tick()
        {
            position += speed;
            _lifeTicks--;
        }
    }
}
