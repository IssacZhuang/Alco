
using System;
using System.IO;
using System.Reflection;
using UniLua;

namespace Vocore.Lua
{
    public class LuaRuntime
    {
        private readonly LuaState luaState;
		
        public LuaRuntime()
        {
            luaState = new LuaState();
            luaState.L_OpenLibs();
        }

        
    }
}

