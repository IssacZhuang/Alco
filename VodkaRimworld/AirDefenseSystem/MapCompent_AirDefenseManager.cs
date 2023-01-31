using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using RimWorld;


namespace ADS
{
    public class MapCompent_AirDefenseManager : MapComponent
    {
        public MapCompent_AirDefenseManager(Map map) : base(map)
        {
        }

        private readonly LinkedList<VisualProjectile> _projectiles = new LinkedList<VisualProjectile>();
        private readonly List<CompTurretAirDefense> _turrets = new List<CompTurretAirDefense>();

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
        }

        public void RegisterProjectile(VisualProjectile entity)
        {
            _projectiles.AddLast(entity);
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
