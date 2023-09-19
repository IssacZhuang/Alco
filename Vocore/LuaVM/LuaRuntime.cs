
using System;
using System.IO;
using System.Reflection;
using UniLua;

namespace Vocore.Lua
{
    public class LuaRuntime
    {
        private readonly ILuaState luaState;
		
        public LuaRuntime()
        {
            luaState = new LuaState();
            luaState.L_OpenLibs();
        }

        public void RunCode(string code)
        {
            ThreadStatus status = luaState.L_DoString(code);
            TryLogErrorText(status);
        }

        public void LoadCode(string path, string code)
        {
            ThreadStatus status = luaState.L_LoadBuffer(code, path);
            TryLogErrorText(status);
        }

        public void Call()
        {
            (luaState as LuaState).L_CallLoaded();
        }



        private void TryLogErrorText(ThreadStatus status)
        {
            string error = TryGetErrorText(status);
            if (error != null)
            {
                Log.Error(error);
            }
        }

        private string TryGetErrorText(ThreadStatus status)
        {
            if (status != ThreadStatus.LUA_OK)
            {
                string error = luaState.ToString(-1);
                luaState.Pop(1);
                return error;
            }
            return null;
        }

    }
}

