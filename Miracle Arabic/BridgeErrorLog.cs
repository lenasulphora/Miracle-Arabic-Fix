using System;
using System.IO;
using System.Text;

namespace Miracle
{
    internal static class BridgeErrorLog
    {
        private static readonly object Gate = new();
        private static string _path = "";

        // Full path of the current session log ("" when logging could not start).
        public static string Path => _path;

        // Deletes any previous log and starts a fresh one with a short header. Called once, at startup.
        public static void StartSession(string directory)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    _path = System.IO.Path.Combine(directory, "chat-via-game-engine-errors.log");
                    if (File.Exists(_path)) File.Delete(_path);
                    File.WriteAllText(_path,
                        "# Chat via game engine (direct send) - error log" + Environment.NewLine +
                        "# Session started: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                        "# Columns: time | level | code | error | cause | mode | typed | source | sent | game says" + Environment.NewLine +
                        "# typed = what you typed, source = text before the output fix, sent = the final text delivered." + Environment.NewLine);
                }
                catch { _path = ""; }
            }
        }

        // One error entry: code, plain-English cause, and the text context of the attempt.
        public static void Error(string code, string cause, string mode,
                                 string? typed, string? source, string? sent, string? detail)
            => Write("ERROR", code, cause, mode, typed, source, sent, detail);

        // Informational entry (for example the automatic fallback to the keyboard route).
        public static void Info(string code, string cause, string mode,
                                string? typed, string? source, string? sent, string? detail)
            => Write("INFO", code, cause, mode, typed, source, sent, detail);

        private static void Write(string level, string code, string cause, string mode,
                                  string? typed, string? source, string? sent, string? detail)
        {
            lock (Gate)
            {
                if (_path.Length == 0) return;
                try
                {
                    var line = new StringBuilder(320);
                    line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    line.Append(" | ").Append(level);
                    line.Append(" | ").Append(code);
                    line.Append(" | ").Append(BridgeErrorCatalog.Name(code));
                    line.Append(" | cause=").Append(Field(cause));
                    line.Append(" | mode=").Append(Field(mode));
                    line.Append(" | typed=").Append(Field(typed));
                    line.Append(" | source=").Append(Field(source));
                    line.Append(" | sent=").Append(Field(sent));
                    if (!string.IsNullOrEmpty(detail)) line.Append(" | game says=").Append(Field(detail));
                    File.AppendAllText(_path, line.Append(Environment.NewLine).ToString());
                }
                catch { /* a log failure must never break sending */ }
            }
        }

        // Flattens a value to one line so a multi-line message can never break the log format.
        private static string Field(string? value)
        {
            string v = (value ?? "").Replace("\r", "\\r").Replace("\n", "\\n").Replace("|", "/");
            return "\"" + v + "\"";
        }
    }
}
