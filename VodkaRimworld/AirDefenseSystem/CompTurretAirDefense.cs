using RimWorld;
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
        private Skyfaller _target = null;
        private MapCompent_AirDefenseManager _entityManager;


        public Vector3 TargetPosition
        {
            get
            {
                if( _target == null)
                {
                    return Vector3.forward;
                }
                return _target.DrawPos;
            }
        }

        public Vector3 TargetDirection
        {
            get
            {
                Vector3 direction = TargetPosition - parent.DrawPos;
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

        public MapCompent_AirDefenseManager EntityManager
        {
            get
            {
                if (parent.Map == null) return null;
                if(_entityManager == null)
                {
                    _entityManager = parent.Map.GetComponent<MapCompent_AirDefenseManager>();
                }
                return _entityManager;
            }
        }

        public CompProperties_TurretAirDefense Props => (CompProperties_TurretAirDefense)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            RegisterToManager();
        }


        public override void PostDeSpawn(Map map)
        {
            DeregisterFromManager();
            base.PostDeSpawn(map);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            DeregisterFromManager();
            base.PostDestroy(mode, previousMap);
        }

        public override void CompTick()
        {
            UpdateMatrix();
            TryShootTarget();
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

        public void UpdateMatrix()
        {
            if (Props.graphicTurretGun == null) return;
            Vector3 drawSize = default;
            drawSize.Set(Props.graphicTurretGun.drawSize.x, 1, Props.graphicTurretGun.drawSize.y);

            _aimingAngle = Mathf.SmoothDampAngle(_aimingAngle, TargetAngle, ref _angleVelocity, Props.smoothTime, Props.maxAimingSpeed);
            _matrix.SetTRS(parent.DrawPos + Altitudes.AltIncVect, Quaternion.AngleAxis(_aimingAngle, Vector3.up), drawSize);
        }

        public void TryShootTarget()
        {
            if (_target == null) return;
            if (_target.GetHeight() < Props.minTargetHeight)
            {
                UntrackSkyfaller();
                return;
            };

        }

        public void LaunchProjectile()
        {

        }

        public void TrackSkyfaller(Skyfaller target)
        {
            _target = target;
        }

        public void UntrackSkyfaller()
        {
            _target = null;
        }

        private void RegisterToManager()
        {
            if (EntityManager == null)
            {
                Log.Error("[Air Defense System] Trying to register turret to a null MapCompent_AirDefenseManager");
                return;
            }
            EntityManager.RegisterTurret(this);
        }

        private void DeregisterFromManager()
        {
            if (EntityManager == null)
            {
                Log.Error("[Air Defense System] Trying to deregister turret to a null MapCompent_AirDefenseManager");
                return;
            }
            EntityManager.DeregisterTurret(this);
            _entityManager = null;
        }
    }
}
