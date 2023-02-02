using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using RimWorld;
using RimWorld.Planet;
using Vocore;
using UnityEngine;

namespace ADS
{
    public class MapCompent_AirDefenseManager : MapComponent
    {
        public MapCompent_AirDefenseManager(Map map) : base(map)
        {
        }

        private readonly LinkedList<VisualProjectile> _projectiles = new LinkedList<VisualProjectile>();
        private readonly List<CompTurretAirDefense> _turrets = new List<CompTurretAirDefense>();
        private readonly List<Skyfaller> _skyfallers = new List<Skyfaller>();

        private readonly List<Skyfaller> _skyfallersToRemove = new List<Skyfaller>();

        public override void MapComponentTick()
        {
            var pointer = _projectiles.First;
            while (pointer != null)
            {
                if (!pointer.Value.IsAlive)
                {
                    var tmp = pointer.Next;
                    _projectiles.Remove(pointer);
                    pointer = tmp;
                }
                if (pointer != null)
                {
                    pointer.Value.Tick();
                    pointer = pointer.Next;
                }
            }

            TryAssignTurret();
        }

        public override void MapComponentUpdate()
        {
            if (WorldRendererUtility.WorldRenderedNow || Find.CurrentMap != this.map) return;

            var pointer = _projectiles.First;
            while (pointer != null)
            {
                var entity = pointer.Value;
                InstancedRenderManager.Default.AddInstance(entity.renderId, entity.position, entity.rotation, entity.size);
                pointer = pointer.Next;
            }
            InstancedRenderManager.Default.Draw();
        }

        public void TryAssignTurret()
        {
            _skyfallersToRemove.Clear();
            for (int i = _skyfallers.Count - 1; i >= 0 ; i--)
            {
                Skyfaller skyfaller = _skyfallers[i];

                if (skyfaller.Destroyed || !skyfaller.Spawned)
                {
                    _skyfallersToRemove.Add(skyfaller);
                }

                foreach(CompTurretAirDefense turret in _turrets)
                {
                    if (skyfaller.InRangeOfTurret(turret)||turret.CanTrackTarget())
                    {
                        _skyfallersToRemove.Add(skyfaller);
                        turret.TrackSkyfaller(skyfaller);
                    }
                }
            }
            _skyfallers.RemoveAll(x => _skyfallersToRemove.Contains(x));
        }

        public void RegisterProjectile(VisualProjectile entity)
        {
            _projectiles.AddLast(entity);
        }

        public void RegisterSkyfaller(Skyfaller skyfaller)
        {
            _skyfallers.Add(skyfaller);
        }

        public void RegisterTurret(CompTurretAirDefense comp)
        {
            _turrets.Add(comp);
        }

        public void DeregisterTurret(CompTurretAirDefense comp)
        {
            _turrets.Remove(comp);
        }
    }
}
