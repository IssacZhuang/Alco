
using System;
using System.IO;


namespace UniLua.Tools
{
    public static class LuaRuntime
    {
        static void Fatal(string msg)
        {
            throw new Exception(msg);
        }

        public static byte[] ComplieCode(string code)
        {
            var lua = LuaAPI.NewState();
            var status = lua.L_LoadString(code);
            if (status != ThreadStatus.LUA_OK)
            {
                Fatal(lua.ToString(-1));
            }
            var cl = ((LuaState)lua).Top.V.ClLValue();
            LuaProto proto = cl.Proto;
            using (MemoryStream stream = new MemoryStream())
            {
                DumpState.Dump(proto, (bytes, start, length) =>
                {
                    stream.Write(bytes, start, length);
                    return DumpStatus.OK;
                }, false);
                return stream.GetBuffer();
            }
        }



        public static LuaProto CompileFile(string filename)
        {
            var lua = LuaAPI.NewState();
            var status = lua.L_LoadFileX(filename, null);
            if (status != ThreadStatus.LUA_OK)
            {
                Fatal(lua.ToString(-1));
            }
            var cl = ((LuaState)lua).Top.V.ClLValue();
            return cl.Proto;
        }
    }
}

