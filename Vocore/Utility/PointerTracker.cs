#define TRACK_POINTER

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Vocore
{
    internal unsafe struct PointerRecord
    {
        public void** tracked;
        public string stackTrace;
    }
    public unsafe class PointerTracker
    {
        private static PointerTracker _instance;
        public static PointerTracker Instance
        {
            get
            {
                if (_instance == null) _instance = new PointerTracker();
                return _instance;
            }
        }

        public static void StaticInitialize()
        {
            _instance = new PointerTracker();
        }

        List<PointerRecord> _tracked;

        public PointerTracker()
        {
            _tracked = new List<PointerRecord>();
        }

        ~PointerTracker()
        {

        }

        public void Track(void** source)
        {
            StackTrace trace = new StackTrace(true);
            PointerRecord record = new PointerRecord
            {
                tracked = source,
                stackTrace = trace.ToString()
            };
            _tracked.Add(record);
        }

        public void DisplayResult()
        {
            PrintRed("Pointer track result:\n");
            for (int i = 0; i < _tracked.Count; i++)
            {
                
            }
        }

        public static void PrintRed(object obj)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(obj.ToString());
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
