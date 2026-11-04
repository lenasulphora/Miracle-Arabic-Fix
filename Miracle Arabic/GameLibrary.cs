using System.Diagnostics;
using System.IO;

namespace Miracle
{
    internal sealed class GameInstance
    {
        // Process id of the running game.
        public int ProcessId { get; set; }

        // Full path of the game executable. This is the identity used to tell forks apart.
        public string ExePath { get; set; } = "";

        // File name of the executable (e.g. "3DXChat.exe"), used when the path cannot be read.
        public string ExeName { get; set; } = "";

        // Window title of the game's main window, used as the visible name.
        public string WindowTitle { get; set; } = "";

        // Main window handle, or IntPtr.Zero for a saved game that is not running.
        public IntPtr WindowHandle { get; set; }
    }

    // Detects the running game builds and resolves a game to its live window by executable path.
    internal static class GameLibrary
    {
        private static readonly string[] GameProcessNames = { "3DXChat" };

        public static bool IsSupportedProcessName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var candidate in GameProcessNames)
                if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static List<GameInstance> DetectRunningGames()
        {
            var games = new List<GameInstance>();
            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch { return games; }

            foreach (var process in processes)
            {
                try
                {
                    if (!IsSupportedProcessName(process.ProcessName)) continue;

                    IntPtr handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero) continue;

                    string exePath = "";
                    try { exePath = process.MainModule?.FileName ?? ""; }
                    catch { /* a protected or mismatched-arch process may refuse the module path */ }

                    games.Add(new GameInstance
                    {
                        ProcessId = process.Id,
                        ExePath = exePath,
                        ExeName = exePath.Length > 0 ? Path.GetFileName(exePath) : process.ProcessName,
                        WindowTitle = (process.MainWindowTitle ?? "").Trim(),
                        WindowHandle = handle
                    });
                }
                catch { /* a game exiting while enumerating is not an error here */ }
                finally { process.Dispose(); }
            }

            games.Sort((a, b) =>
            {
                int byTitle = string.Compare(a.WindowTitle, b.WindowTitle, StringComparison.OrdinalIgnoreCase);
                return byTitle != 0 ? byTitle : string.Compare(a.ExePath, b.ExePath, StringComparison.OrdinalIgnoreCase);
            });
            return games;
        }

        public static IntPtr FindWindowByExePath(string? exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return IntPtr.Zero;
            string want = NormalizePath(exePath);
            foreach (var game in DetectRunningGames())
            {
                if (game.WindowHandle != IntPtr.Zero && NormalizePath(game.ExePath) == want)
                    return game.WindowHandle;
            }
            return IntPtr.Zero;
        }

        public static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            string value;
            try { value = Path.GetFullPath(path); }
            catch { value = path; }
            return value.TrimEnd('\\', '/').ToLowerInvariant();
        }
    }
}
