namespace LouveSystems.K2.BotLib
{
    using MoonSharp.Interpreter;
    using System;

    internal static class LuaHelper
    {
        public static Func<DynValue> ToLuaFunction<T>(this Func<T> func, Script script)
        {
            return () => DynValue.FromObject(script, func());
        }
    }
}
