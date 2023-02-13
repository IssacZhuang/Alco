using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using UnityEngine;

namespace MuzzleFlash
{
    public class WeaponPositionCache : GameComponent
    {
        private static readonly Dictionary<Thing, Vector3> _drawPos = new Dictionary<Thing, Vector3>();

        public WeaponPositionCache(Game _)
        {

        }

        private int _timer = 0;
        private const int _collectionInterval = 10800;

        public static void SetDrawPos(Thing eq, Vector3 pos)
        {
            _drawPos[eq] = pos;
        }

        public static Vector3 GetDrawPos(Thing eq)
        {
            Vector3 result = _drawPos.GetWithFallback(eq);
            return result;
        }

        public override void GameComponentTick()
        {
            _timer++;
            if (_timer >= _collectionInterval)
            {
                _drawPos.Clear();
                _timer = 0;
            }
        }
    }
}
