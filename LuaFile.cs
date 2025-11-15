namespace LouveSystems.K2.BotLib
{
    using System;

    public readonly struct LuaFile
    {
        public readonly string name;
        public readonly Func<string> getText;

        public LuaFile(string name, Func<string> getText)
        {
            this.name = name;
            this.getText = getText;
        }
    }
}
