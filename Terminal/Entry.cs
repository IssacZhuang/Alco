using System;
using System.Collections.Generic;
using RuntimeAssemblyLoader;
using UnityEngine;

namespace Terminal
{
    public class Entry : IEntry
    {
        public int ExecuteOder => -100;

        void IEntry.Entry()
        {
            GameObject cam = Camera.main.gameObject;
            Terminal terminal = cam.AddComponent<Terminal>();
        }
    }
}

