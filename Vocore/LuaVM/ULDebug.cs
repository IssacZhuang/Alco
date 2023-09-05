
using System;
using Logger = Vocore.Log;

namespace UniLua.Tools
{
	public class LuaLogger
	{
        public static void Log(object obj)
        {
            Logger.Info(obj);
        }
        public static void LogError(object obj)
        {
            Logger.Error(obj);
		}
	}
}

