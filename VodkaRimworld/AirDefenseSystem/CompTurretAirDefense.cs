using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;


namespace ADS
{
    public class CompTurretAirDefense: ThingComp
    {
        private float _aimingAngle = 0f;
        private float _angleVelocity = 0f;
        private Matrix4x4 _matrix = default;
        private Thing _target = null;

        public Thing Target
        {
            get
            {
                return _target;
            }
            set
            {
                _target = value;
            }
        }

        public Vector3 TargetDirection
        {
            get
            {
                Vector3 direction = UI.MouseMapPosition() - parent.DrawPos;
                direction.y = 0;
                return direction;
            }
        }

        public float TargetAngle
        {
            get
            {
                return TargetDirection.AngleFlat();
            }
        }

        public CompProperties_TurretAirDefense Props => (CompProperties_TurretAirDefense)props;

        public override void CompTick()
        {
            if (Props.graphicTurretGun == null) return;
            Vector3 drawSize = default;
            drawSize.Set(Props.graphicTurretGun.drawSize.x, 1, Props.graphicTurretGun.drawSize.y);

            _aimingAngle = Mathf.SmoothDampAngle(_aimingAngle, TargetAngle, ref _angleVelocity, Props.smoothTime, Props.maxAimingSpeed);
            _matrix.SetTRS(parent.DrawPos, Quaternion.AngleAxis(_aimingAngle,Vector3.up), drawSize);
        }

        public override void PostDraw()
        {
            if (Props.graphicTurretGun == null) return;
            Graphics.DrawMesh(MeshPool.plane10, _matrix, Props.MatTurretGunInt, 0);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<float>(ref _aimingAngle, "ADS_aimingAngle");
        }
    }
}
