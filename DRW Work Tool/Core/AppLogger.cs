using System;
using System.IO;
using System.Text;

namespace DRW_Work_Tool.Core
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Success
    }

    public sealed class LogEntry
    {
        public DateTime Time { get; }
        public LogLevel Level { get; }
        public string Text { get; }

        public LogEntry(
            DateTime time,
            LogLevel level,
            string text)
        {
            Time = time;
            Level = level;
            Text = text;
        }

        public string ToFileLine()
        {
            return
                $"[{Time:yyyy-MM-dd HH:mm:ss.fff}] " +
                $"[{Level.ToString().ToUpperInvariant()}] " +
                Text;
        }
    }

    public static class AppLogger
    {
        private static readonly object Sync = new();

        // Mantido para compatibilidade com código antigo.
        public static event Action<string>? MessageLogged;

        // Novo evento usado pela UI para aplicar cor por severidade.
        public static event Action<LogEntry>? EntryLogged;

        public static void Log(string message) =>
            Write(LogLevel.Info, message);

        public static void Success(string message) =>
            Write(LogLevel.Success, message);

        public static void Warning(string message) =>
            Write(LogLevel.Warning, message);

        public static void Error(string message) =>
            Write(LogLevel.Error, message);

        public static void ErrorDetailed(
            string operation,
            string reason,
            string solution)
        {
            string message =
                $"{operation} FALHOU." +
                Environment.NewLine +
                Environment.NewLine +
                "Motivo:" +
                Environment.NewLine +
                reason +
                Environment.NewLine +
                Environment.NewLine +
                "Possível solução:" +
                Environment.NewLine +
                solution +
                Environment.NewLine;

            Write(LogLevel.Error, message);
        }

        public static void Separator()
        {
            Write(
                LogLevel.Info,
                new string('-', 72));
        }

        private static void Write(
            LogLevel level,
            string message)
        {
            DateTime now = DateTime.Now;
            LogEntry entry =
                new(now, level, message);

            string line = entry.ToFileLine();

            lock (Sync)
            {
                AppPaths.EnsureWorkspace();

                File.AppendAllText(
                    AppPaths.LogFile,
                    line + Environment.NewLine,
                    new UTF8Encoding(false));
            }

            MessageLogged?.Invoke(line);
            EntryLogged?.Invoke(entry);
        }
    }
}
