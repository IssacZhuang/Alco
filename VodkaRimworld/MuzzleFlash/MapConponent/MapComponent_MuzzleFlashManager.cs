using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using Vocore;
using Vocore.AssetsLib;
using UnityEngine;

namespace MuzzleFlash
{
    public class MapComponent_MuzzleFlashManager:MapComponent
    {
        private readonly LinkedList<MuzzleFlashEntity> _entities;
        public MapComponent_MuzzleFlashManager(Map map) : base(map)
        {
            _entities = new LinkedList<MuzzleFlashEntity>();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            var pointer = _entities.First;
            while (pointer != null)
            {
                if (!pointer.Value.IsAlive)
                {
                    var tmp = pointer.Next;
                    _entities.Remove(pointer);
                    pointer = tmp;
                }
                if (pointer != null)
                {
                    pointer.Value.Tick();
                    pointer = pointer.Next;
                }
            }
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            var pointer = _entities.First;
            while (pointer != null)
            {
                var entity = pointer.Value;
                AnimatedRenderManager.Default.AddInstance(entity.def.RenderID, entity.position, entity.rotation, entity.size, Color.white, entity.frame);
                pointer = pointer.Next;
            }
            AnimatedRenderManager.Default.Draw();
        }

        public void RegisterEntity(MuzzleFlashEntity entity)
        {
            _entities.AddLast(entity);
        }
    }
}
