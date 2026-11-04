using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Miracle
{
    internal readonly struct BridgeAnswer
    {
        public BridgeAnswer(int status, string? text)
        {
            Status = status;
            Text = text ?? string.Empty;
        }

        public int Status { get; }

        public string Text { get; }

        public bool Ok => Status == ChatBridgeClient.StatusOk;
    }

    // The real chat bridge is a small native DLL that gets loaded inside the game process. That
    // part is not part of this repository, so here the bridge route simply reports "not available"
    // and the app falls back to the keyboard route.
    internal static class ChatBridgeClient
    {
        public const int StatusOk = 0;
        public const int StatusNotReady = 1;
        public const int StatusMonoMissing = 2;
        public const int StatusDomainMissing = 3;
        public const int StatusClassMissing = 4;
        public const int StatusFieldMissing = 5;
        public const int StatusInputMissing = 6;
        public const int StatusMethodMissing = 7;
        public const int StatusManagedException = 8;
        public const int StatusBadRequest = 9;

        public static string FindBridgeDll() => string.Empty;

        public static bool EnsureOpen(int pid) => false;

        public static bool TryAttach(int pid) => false;

        public static BridgeAnswer Ping(int pid, int timeoutMs = 4000)
            => new BridgeAnswer(StatusNotReady, "bridge not included");

        public static BridgeAnswer Send(int pid, string text, int timeoutMs = 15000)
            => new BridgeAnswer(StatusNotReady, "bridge not included");

        public static bool IsModuleLoaded(int pid, string moduleName)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                foreach (ProcessModule module in process.Modules)
                {
                    if (string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        public static int ProcessIdOfWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(hwnd, out uint pid);
            return (int)pid;
        }
    }
}