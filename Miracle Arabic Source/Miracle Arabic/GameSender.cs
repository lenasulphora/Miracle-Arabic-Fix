using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Miracle
{
    // How Easy Mode text is delivered to the game window.
    public enum SendMode
    {
        Auto = 0,

        // Bring the game to the foreground, paste into it and press Enter, via SendInput.
        Keyboard = 1,

        Direct = 2,

        ChatDirect = 3,

        Bridge = 4
    }

    public enum SendResult
    {
        // No transport attempted yet (the value a new cycle starts from).
        NotAttempted = 0,

        // Keyboard path: the text was pasted into the game's chat and Enter was pressed to send it.
        Sent,

        // Game-engine path: the game's own send call ran and the line was sent.
        SentDirect,

        // Game-engine path refused *before* emitting anything (bridge/module unusable).
        DirectUnavailable,

        // Game-engine path ran but delivery could not be proven.
        DirectUnverified,

        // Target window not found.
        TargetNotFound,

        // Focus could not be brought to the target window.
        FocusFailed,

        // Aborted before emitting any input (clipboard, switching, ...).
        Failed
    }

    internal static class GameSender
    {
        private const uint VK_RETURN = 0x0D, VK_CONTROL = 0x11, VK_V = 0x56, VK_MENU = 0x12;
        private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008, MAPVK_VK_TO_VSC = 0;
        // How long the keyboard path waits for the foreground switch to take effect.
        private const int FOCUS_MS = 900;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint uCode, uint uMapType);
        private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public INPUTUNION u; }

        private static IntPtr _target = IntPtr.Zero;
        private static string _targetTitle = "";
        private static string _targetExe = "";

        public static IntPtr ResolveTarget(string? title, string? exePath = null)
        {
            title = (title ?? "").Trim();
            exePath = (exePath ?? "").Trim();
            if (title.Length == 0 && exePath.Length == 0) return IntPtr.Zero;

            if (_target != IntPtr.Zero && IsWindow(_target)
                && string.Equals(_targetTitle, title, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_targetExe, exePath, StringComparison.OrdinalIgnoreCase))
                return _target;

            IntPtr h = exePath.Length > 0
                ? GameLibrary.FindWindowByExePath(exePath)
                : FindWindowByTitle(title);

            _target = h;
            _targetTitle = h == IntPtr.Zero ? "" : title;
            _targetExe = h == IntPtr.Zero ? "" : exePath;
            return h;
        }

        private static IntPtr FindWindowByTitle(string title)
        {
            if (title.Length == 0) return IntPtr.Zero;

            IntPtr h = FindWindow(null, title);
            if (h != IntPtr.Zero) return h;

            string want = title.ToLowerInvariant();
            IntPtr best = IntPtr.Zero;
            EnumWindows((w, _) =>
            {
                if (!IsWindowVisible(w)) return true;
                var sb = new StringBuilder(512);
                if (GetWindowText(w, sb, sb.Capacity) <= 0) return true;
                string t = sb.ToString().Trim().ToLowerInvariant();
                if (t.Length == 0) return true;
                if (t == want) { best = w; return false; }
                if (best == IntPtr.Zero && t.StartsWith(want, StringComparison.Ordinal)) best = w;
                return true;
            }, IntPtr.Zero);
            return best;
        }

        // Drop the cached window (called when the target title or game install changes).
        public static void ForgetTarget() { _target = IntPtr.Zero; _targetTitle = ""; _targetExe = ""; }

        public static bool IsGameEngineTarget(IntPtr target)
        {
            if (target == IntPtr.Zero || !IsWindow(target)) return false;
            if (GetWindowThreadProcessId(target, out uint pid) == 0 || pid == 0) return false;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                return GameLibrary.IsSupportedProcessName(process.ProcessName)
                       && ChatBridgeClient.IsModuleLoaded((int)pid, "mono.dll");
            }
            catch { return false; }
        }

        public static async Task<SendResult> SendAsync(
            string text, SendMode mode, int speedMs,
            Func<string, Task> stepAside, Func<Task> stepBack,
            string typedText = "", string sourceText = "")
        {
            try
            {
                if (mode == SendMode.Keyboard)
                    return await SendKeyboardAsync(text, speedMs, stepAside, stepBack).ConfigureAwait(true);

                if (mode == SendMode.ChatDirect) mode = SendMode.Bridge;
                if (mode == SendMode.Direct) mode = SendMode.Auto;

                if (mode == SendMode.Bridge)
                {
                    return await SendBridgeAsync(text, "Chat via game engine", typedText, sourceText).ConfigureAwait(true);
                }

                if (IsGameEngineTarget(_target))
                {
                    SendResult bridged = await SendBridgeAsync(text, "Automatic", typedText, sourceText).ConfigureAwait(true);
                    if (bridged != SendResult.DirectUnavailable) return bridged;
                    // The bridge failed cleanly and the cause is already logged; record the retry.
                    BridgeErrorLog.Info("BRG-017", "Bridge unavailable for this attempt; retrying with Keyboard keys",
                        "Automatic", typedText, sourceText, text, null);
                }
                return await SendKeyboardAsync(text, speedMs, stepAside, stepBack).ConfigureAwait(true);
            }
            catch { return SendResult.Failed; }
        }

        private static void PasteNow()
        {
            var inputs = new[] { Key(VK_CONTROL, false), Key(VK_V, false), Key(VK_V, true), Key(VK_CONTROL, true) };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        // Press Enter to send the line that was just pasted into the chat.
        private static void SubmitNow()
        {
            var inputs = new[] { Key(VK_RETURN, false), Key(VK_RETURN, true) };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static INPUT Key(uint vk, bool up) => new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC),
                    dwFlags = (up ? KEYEVENTF_KEYUP : 0u) | KEYEVENTF_SCANCODE
                }
            }
        };

        private static async Task<bool> SetClipboardAsync(string text)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (await ClipboardWorker.SetTextAsync(text).ConfigureAwait(true)) return true;
                await Task.Delay(15).ConfigureAwait(true);
            }
            return false;
        }

        private static async Task<bool> FocusTargetAsync(IntPtr h)
        {
            SetForegroundWindow(h);
            int waited = 0;
            bool nudged = false;
            while (GetForegroundWindow() != h && waited < FOCUS_MS)
            {
                await Task.Delay(30).ConfigureAwait(true);
                waited += 30;
                if (!nudged && waited >= 120)
                {
                    nudged = true;
                    SendInput(2, new[] { Key(VK_MENU, false), Key(VK_MENU, true) }, Marshal.SizeOf(typeof(INPUT)));
                }
                SetForegroundWindow(h);
            }
            return GetForegroundWindow() == h;
        }

        private static async Task<SendResult> SendBridgeAsync(
            string text, string modeLabel, string typedText, string sourceText)
        {
            int pid = ChatBridgeClient.ProcessIdOfWindow(_target);

            if (pid == 0 || !ChatBridgeClient.IsModuleLoaded(pid, "mono.dll"))
            {
                BridgeErrorLog.Error("BRG-002",
                    "The selected game has no Mono runtime (IL2CPP build); the game-engine route is not used",
                    modeLabel, typedText, sourceText, text, null);
                return SendResult.DirectUnavailable;
            }

            BridgeAnswer answer;
            bool attachFailed = false;
            string attachCode = "BRG-001";
            string attachCause = "The bridge is not ready";
            try
            {
                answer = await Task.Run(() =>
                {
                    if (ChatBridgeClient.TryAttach(pid)) return ChatBridgeClient.Send(pid, text);
                    // Attaching failed: capture why before the ping reports the generic "not ready".
                    attachFailed = true;
                    (attachCode, attachCause) = DescribeAttachFailure(pid);
                    return ChatBridgeClient.Ping(pid);
                }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                BridgeErrorLog.Error("BRG-016", ex.Message, modeLabel, typedText, sourceText, text, null);
                return SendResult.DirectUnverified;
            }

            return MapBridgeAnswer(answer, attachFailed, attachCode, attachCause, modeLabel, typedText, sourceText, text);
        }

        private static SendResult MapBridgeAnswer(BridgeAnswer answer, bool attachFailed,
            string attachCode, string attachCause, string modeLabel,
            string typedText, string sourceText, string sentText)
        {
            switch (answer.Status)
            {
                case ChatBridgeClient.StatusOk:
                    return SendResult.SentDirect;

                case ChatBridgeClient.StatusMonoMissing:
                    LogBridge("BRG-002", "The game's Mono runtime could not be located", modeLabel, typedText, sourceText, sentText, answer);
                    return SendResult.DirectUnavailable;

                case ChatBridgeClient.StatusDomainMissing:
                    LogBridge("BRG-003", "The game's .NET domain/assembly could not be read", modeLabel, typedText, sourceText, sentText, answer);
                    return SendResult.DirectUnavailable;

                case ChatBridgeClient.StatusClassMissing:
                    LogBridge("BRG-004", "The in-game chat class (GUIchat) was not found", modeLabel, typedText, sourceText, sentText, answer);
                    return SendResult.DirectUnavailable;

                case ChatBridgeClient.StatusFieldMissing:
                    LogBridge("BRG-005", "The in-game chat text field was not found", modeLabel, typedText, sourceText, sentText, answer);
                    return SendResult.DirectUnavailable;

                case ChatBridgeClient.StatusInputMissing:
                    LogBridge("BRG-006", "The in-game chat input box was not found (still loading or not in a room)", modeLabel, typedText, sourceText, sentText, answer);
                    return SendResult.DirectUnavailable;

                case ChatBridgeClient.StatusMethodMissing:
                    LogBridge("BRG-007", "A required chat method was not found in the game", modeLabel, typedText, sourceText, sentText, answer);
                    return SendResult.DirectUnavailable;

                case ChatBridgeClient.StatusNotReady:
                    // An attach that never became ready is the most useful cause to report here.
                    if (attachFailed)
                        LogBridge(attachCode, attachCause, modeLabel, typedText, sourceText, sentText, answer);
                    else
                        LogBridge("BRG-001", "The bridge is attached but not ready", modeLabel, typedText, sourceText, sentText, answer);
                    return SendResult.DirectUnavailable;

                case ChatBridgeClient.StatusBadRequest:
                    LogBridge("BRG-009", "The bridge rejected the request", modeLabel, typedText, sourceText, sentText, answer);
                    return SendResult.DirectUnverified;

                case ChatBridgeClient.StatusManagedException:
                    LogBridge("BRG-008", "The game's own send call raised an exception", modeLabel, typedText, sourceText, sentText, answer);
                    return SendResult.DirectUnverified;

                default:
                    if (answer.Status < 0)
                        LogBridge("BRG-011", "The game's UI thread did not answer within the timeout", modeLabel, typedText, sourceText, sentText, answer);
                    else
                        LogBridge("BRG-010", "A guarded fault or unknown status came back from the game", modeLabel, typedText, sourceText, sentText, answer);
                    return SendResult.DirectUnverified;
            }
        }

        private static void LogBridge(string code, string cause, string modeLabel,
            string typedText, string sourceText, string sentText, BridgeAnswer answer)
            => BridgeErrorLog.Error(code, cause, modeLabel, typedText, sourceText, sentText, answer.Text);

        private static (string Code, string Cause) DescribeAttachFailure(int pid)
        {
            if (pid == 0)
                return ("BRG-012", "No game process is running for the selected target");
            if (string.IsNullOrEmpty(ChatBridgeClient.FindBridgeDll()))
                return ("BRG-013", "MiracleChatBridge.dll was not found beside the tool or in the ChatBridge build output");
            if (!ChatBridgeClient.EnsureOpen(pid))
                return ("BRG-015", "The shared memory section could not be created or opened");
            return ("BRG-014", "Injection into the game failed or the module never became ready");
        }

        private static async Task<SendResult> SendKeyboardAsync(
            string text, int speedMs, Func<string, Task> stepAside, Func<Task> stepBack)
        {
            IntPtr h = _target;
            if (h == IntPtr.Zero) return SendResult.TargetNotFound;

            int settle = Math.Clamp(speedMs, 10, 1500);
            if (!await SetClipboardAsync(text).ConfigureAwait(true)) return SendResult.Failed;

            await stepAside(text).ConfigureAwait(true);
            try
            {
                if (!await FocusTargetAsync(h).ConfigureAwait(true))
                {
                    return SendResult.FocusFailed;
                }

                await Task.Delay(Math.Clamp(settle / 3, 30, 150)).ConfigureAwait(true);
                PasteNow();
                await Task.Delay(Math.Clamp(settle, 60, 400)).ConfigureAwait(true);
                SubmitNow();
                await Task.Delay(20).ConfigureAwait(true);
                return SendResult.Sent;
            }
            finally
            {
                await stepBack().ConfigureAwait(true);
            }
        }

        public static void PasteIntoForeground() => PasteNow();
    }

    public readonly struct SendOutcome
    {
        // What the transport actually did.
        public SendResult Result { get; }

        // True when the transport emitted something (key presses or a bridge send).
        public bool Emitted { get; }

        // True when the line was confirmed as sent.
        public bool Confirmed { get; }

        // True only when the game may be holding a message of unknown fate.
        public bool Uncertain => Emitted && !Confirmed;

        public bool SafeToReuse => !Emitted;

        private SendOutcome(SendResult result, bool emitted, bool confirmed)
        {
            Result = result; Emitted = emitted; Confirmed = confirmed;
        }

        public static SendOutcome From(SendResult result) => new SendOutcome(result,
            // A refused or unresolvable attempt proves nothing was emitted.
            emitted: result != SendResult.NotAttempted
                     && result != SendResult.DirectUnavailable
                     && result != SendResult.TargetNotFound
                     && result != SendResult.FocusFailed
                     && result != SendResult.Failed,
            confirmed: result == SendResult.Sent || result == SendResult.SentDirect);
    }
}
