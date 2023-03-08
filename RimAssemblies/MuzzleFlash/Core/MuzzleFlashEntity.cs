using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;
using Verse;
using Vocore.EmbeddedAssets;

namespace MuzzleFlash
{
    public class MuzzleFlashEntity
    {
        private int _lifeTicks = 60;

        public readonly MuzzleFlashDef def;

        public float frame;

        public Vector3 position;
        public Quaternion rotation;
        public Vector3 size;

        public int LifeTick => _lifeTicks;
        public bool IsAlive => _lifeTicks > 0;

        public MuzzleFlashEntity(MuzzleFlashDef def,Vector3 pos, float angle, Vector2 size2D)
        {
            _lifeTicks = def.duration;
            this.def = def;
            position = pos;
            rotation = Quaternion.AngleAxis((angle - 90f) % 360f, Vector3.up);
            size = new Vector3(size2D.x, 1f, size2D.y);
            frame = Mathf.Floor(Rand.Range(0, def.splits.y)) * (def.framesPerAnimation);
        }

        public void Tick()
        {
            _lifeTicks--;
            frame += (def.framesPerAnimation - 1) / def.duration;
        }

        public override string ToString()
        {
            return position + "\n" + size + "\n" + frame;
        }
    }
}
