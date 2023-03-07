using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using UnityEngine;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Security.Policy;

namespace AssemblyHotReload
{
    public class AssemblyLoader : MarshalByRefObject
    {
        public Assembly Load(string path)
        {
            ValidatePath(path);

            return Assembly.Load(path);
        }

        public Assembly LoadFrom(string path)
        {
            ValidatePath(path);

            return Assembly.LoadFrom(path);
        }

        private void ValidatePath(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (!System.IO.File.Exists(path))
                throw new ArgumentException(String.Format("path \"{0}\" does not exist", path));
        }
    }
}

