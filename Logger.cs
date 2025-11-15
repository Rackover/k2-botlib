
namespace LouveSystems.K2.BotLib
{
    using System;
    using System.Runtime.CompilerServices;

    public static class Logger
    {
        public delegate void OnLogDelegate(int level, object msgs, string filePath);
        public static event OnLogDelegate OnLog;

        static Logger()
        {
        } 

        public static void Trace(object msgs, [CallerFilePath] string filePath = "")
        {
            OnLog?.Invoke(0, msgs, filePath);
        }

        public static void Debug(object msgs, [CallerFilePath] string filePath = "")
        {
            OnLog?.Invoke(1, msgs, filePath);
        }

        public static void Info(object msgs, [CallerFilePath] string filePath = "")
        {
            OnLog?.Invoke(2, msgs, filePath);
        }

        public static void Warn(object msgs, [CallerFilePath] string filePath = "")
        {
            OnLog?.Invoke(3, msgs, filePath);
        }

        public static void Error(object msgs, [CallerFilePath] string filePath = "")
        {
            OnLog?.Invoke(4, msgs, filePath);
        }
    }
}