using Vocore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using System.Threading;

using UnityEngine;
using System.Threading.Tasks;

using Unity.Mathematics;
using System.IO;

using Vocore.Lua;
using Mond;

namespace Vocore.Test
{
    delegate void TestDelegate();

    public class Playground
    {
        //some temp code for testing
        [Test("Playground")]
        public unsafe void Test()
        {
            // string filename = "test.zip";
            // string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
            // Log.Info("Path: " + path);
            // using (ResourcePack pack = new ResourcePack(path))
            // {
            //     pack.TrySetFile("test.bin", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            //     pack.TrySetTextFile("test.txt", "Hello World!");
            // }

            string luaCode =@"
for i=1, 10000000 do
    local a = 1+2*3/4
end
            ";
            LuaRuntime lua = new LuaRuntime();
            lua.RunCode("");

            UnitTest.Benchmark("lua complie",()=>{
                lua.LoadCode("path",luaCode);
            });

            UnitTest.Benchmark("lua run",()=>{
                lua.Call();
            });

            string mondOCde = @"
for(var i=0;i<10000000;i++){

}
            ";

            MondProgram program = null;
            MondState mond = new MondState();

            mond.Run("");
            UnitTest.Benchmark("mond complie",()=>{
                program = MondProgram.Compile(mondOCde);
            });
            
            UnitTest.Benchmark("mond run",()=>{
                mond.Load(program);
            });

        }

        public void TestGeneric<T>(T data)
        {
            //Log.Info("TestGeneric: " + data);
        }
    }

}

