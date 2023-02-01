using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vocore;
using UnityEngine;
using Verse;

namespace ADS
{
    public class CompProperties_TurretAirDefense:CompProperties
    {
        public CompProperties_TurretAirDefense()
        {
            compClass = typeof(CompTurretAirDefense);
        }

        public GraphicData graphicTurretGun;
        public GraphicData graphicProjectile;
        public float smoothTime = 0.1f;
        public float maxAimingSpeed = 3600f;
        public float minTargetHeight = 3f;
        public float range = 75;
        public float preAimFactor = 15f;

        private int _projectileRenderID = -1;

        public Material MatTurretGunInt
        {
            get
            {
                if (graphicTurretGun == null) return null;
                return graphicTurretGun.Graphic.MatSingle;
            }
        }

        public Material MatProjectileInt
        {
            get
            {
                if (graphicProjectile == null) return null;
                return graphicProjectile.Graphic.MatSingle;
            }
        }

        public int ProjectileRenderID
        {
            get {
                if (MatProjectileInt == null)
                {
                    Log.Error("[Air Defense System] null graphicProjectile of CompProperties_TurretAirDefense");
                    return -1;
                }
                if (_projectileRenderID < 0)
                {
                    MatProjectileInt.enableInstancing = true;
                    _projectileRenderID = InstancedRenderManager.Default.GetRendererID(MeshPool.plane10, MatTurretGunInt);
                }
                return _projectileRenderID;
             }
        }
    }
}
