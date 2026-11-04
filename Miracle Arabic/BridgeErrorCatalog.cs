using System;
using System.IO;
using System.Text;

namespace Miracle
{
    internal static class BridgeErrorCatalog
    {
        public readonly struct Entry
        {
            public Entry(string code, string name, string meaning, string cause, string action)
            {
                Code = code; Name = name; Meaning = meaning; Cause = cause; Action = action;
            }

            public string Code { get; }
            public string Name { get; }
            public string Meaning { get; }
            public string Cause { get; }
            public string Action { get; }
        }

        public static readonly Entry[] Entries =
        {
            new Entry("BRG-001", "BRIDGE_NOT_READY",
                "The chat module is attached but not ready to talk yet.",
                "The module was not injected yet, the game just started, or the previous session was dropped.",
                "Open the game and enter a room, then send again."),
            new Entry("BRG-002", "MONO_RUNTIME_MISSING",
                "The game's Mono runtime (mono.dll) could not be located or opened.",
                "Unsupported game build, or a 32/64-bit mismatch.",
                "Use a game build the bridge supports; report the build if it persists."),
            new Entry("BRG-003", "MONO_DOMAIN_MISSING",
                "The game's .NET domain / Assembly-CSharp could not be read.",
                "The game is still loading, or the build differs from the supported one.",
                "Wait until the game is fully loaded, then retry."),
            new Entry("BRG-004", "CHAT_CLASS_MISSING",
                "The in-game chat class (GUIchat) was not found.",
                "Game version mismatch, or you are not inside the world/room yet.",
                "Be inside a room; update the bridge if the game changed."),
            new Entry("BRG-005", "CHAT_FIELD_MISSING",
                "The in-game chat text field (chatText) was not found.",
                "The chat UI is not ready, or the game build differs.",
                "Open the chat box, then retry."),
            new Entry("BRG-006", "CHAT_INPUT_MISSING",
                "The in-game chat input box was not found.",
                "The game is still loading, or you are not inside a room.",
                "Open the chat box and enter a room, then retry."),
            new Entry("BRG-007", "CHAT_METHOD_MISSING",
                "A required chat method (send or text accessor) was not found.",
                "Game version mismatch.",
                "Update the bridge to match the game build."),
            new Entry("BRG-008", "GAME_CODE_EXCEPTION",
                "The game's own sending code raised an exception.",
                "The game rejected the text or its current state.",
                "The line may or may not have been sent - check the game before resending."),
            new Entry("BRG-009", "BAD_REQUEST",
                "The bridge rejected the request.",
                "Unknown operation or a mismatched protocol between tool and module.",
                "Update both the tool and the bridge together."),
            new Entry("BRG-010", "BRIDGE_FAULT",
                "A guarded fault or an unknown status came back from the game.",
                "Incompatible memory layout inside the game against the bridge.",
                "Retry once; if it repeats, report it with this log."),
            new Entry("BRG-011", "GAME_NO_ANSWER",
                "The game's UI thread did not answer within the timeout.",
                "The game is frozen, busy, or minimised so its message loop did not run.",
                "Click on the game window, then send again."),
            new Entry("BRG-012", "GAME_NOT_RUNNING",
                "No process was found for the selected game.",
                "The selected game is closed, or its window is not up yet.",
                "Start the selected game, or pick another one in settings."),
            new Entry("BRG-013", "BRIDGE_DLL_NOT_FOUND",
                "MiracleChatBridge.dll is missing.",
                "Incomplete install or publish of the tool.",
                "Reinstall/re-publish so the DLL sits beside the tool."),
            new Entry("BRG-014", "INJECTION_FAILED",
                "Injecting the chat module into the game failed.",
                "Permissions, antivirus, or process protection blocked the injection.",
                "Run the tool as administrator and allow it in your antivirus."),
            new Entry("BRG-015", "SHARED_MEMORY_UNAVAILABLE",
                "The shared memory section could not be created or opened.",
                "A stale section or a permissions problem.",
                "Restart both the tool and the game."),
            new Entry("BRG-016", "SEND_PATH_EXCEPTION",
                "An unexpected error happened inside the tool's bridge-send code.",
                "An internal bug in the tool.",
                "Report it with this log."),
            new Entry("BRG-017", "AUTO_FALLBACK_TO_KEYBOARD",
                "Automatic mode retried with Keyboard keys after the bridge failed cleanly.",
                "The chat module was unusable for this attempt.",
                "Informational only - no action needed; the line above shows the real cause."),
        };

        // Human name for a code, used on every log line.
        public static string Name(string code)
        {
            foreach (var entry in Entries)
                if (entry.Code == code) return entry.Name;
            return "UNKNOWN";
        }

        // Writes the decode list to <Downloads>\Miracle-ChatViaGameEngine-Errors.csv (best effort).
        public static void WriteCsvToDownloads()
        {
            try
            {
                string downloads = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloads)) return;

                var csv = new StringBuilder();
                csv.Append("Code,Name,Meaning,Likely Cause,What To Do").Append(Environment.NewLine);
                foreach (var entry in Entries)
                {
                    csv.Append(Cell(entry.Code)).Append(',')
                       .Append(Cell(entry.Name)).Append(',')
                       .Append(Cell(entry.Meaning)).Append(',')
                       .Append(Cell(entry.Cause)).Append(',')
                       .Append(Cell(entry.Action)).Append(Environment.NewLine);
                }
                File.WriteAllText(System.IO.Path.Combine(downloads, "Miracle-ChatViaGameEngine-Errors.csv"), csv.ToString());
            }
            catch { /* the reference file is best effort */ }
        }

        private static string Cell(string? value)
        {
            string v = (value ?? "").Replace("\"", "\"\"");
            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v + "\"" : v;
        }
    }
}
