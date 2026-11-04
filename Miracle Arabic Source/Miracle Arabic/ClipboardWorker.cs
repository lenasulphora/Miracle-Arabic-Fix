using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Miracle
{
    internal static class ClipboardWorker
    {
        private const int GraceMs = 1200, MaxWorkers = 3;
        private const uint QS_ALLINPUT = 0x04FF, PM_REMOVE = 0x0001;

        [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG m, IntPtr h, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] private static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint timeout, uint wakeMask);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

        private static readonly object Gate = new object();
        private static Worker? _current;
        private static int _spawned;
        // One worker: an STA thread with a job queue that pumps Win32 messages between jobs.
        private sealed class Worker
        {
            private readonly ConcurrentQueue<Job> _queue = new ConcurrentQueue<Job>();
            private readonly AutoResetEvent _signal = new AutoResetEvent(false);

            // Set when this worker is retired, so no new jobs are handed to it.
            public volatile bool Retired;

            // Set while a job is inside an OLE call, so a stuck worker can be detected.
            private long _busySince;

            public Worker()
            {
                var t = new Thread(Loop) { IsBackground = true, Name = "MiracleClipboard" };
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }

            // Queues a job and returns a task that completes when it runs.
            public Task<(bool ok, string? text)> Submit(Func<(bool ok, string? text)> work)
            {
                var tcs = new TaskCompletionSource<(bool, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
                _queue.Enqueue(new Job(work, tcs));
                try { _signal.Set(); } catch { }
                return tcs.Task;
            }

            // Worker loop: pump messages, run queued jobs, then pump messages again.
            private void Loop()
            {
                IntPtr[] handles = { _signal.SafeWaitHandle.DangerousGetHandle() };
                var pending = new Job[Job.Batch];
                int count = 0;
                while (true)
                {
                    PumpMessages();
                    if (count == 0)
                        while (count < pending.Length && _queue.TryDequeue(out Job queued)) pending[count++] = queued;

                    if (count == 0)
                    {
                        MsgWaitForMultipleObjects(1, handles, false, 250, QS_ALLINPUT);
                        continue;
                    }

                    Job job = pending[0];
                    Array.Copy(pending, 1, pending, 0, --count);
                    Volatile.Write(ref _busySince, DateTime.UtcNow.Ticks);
                    try { job.Tcs.TrySetResult(job.Work()); }
                    catch { job.Tcs.TrySetResult((false, null)); }
                    finally { Volatile.Write(ref _busySince, 0); }
                    PumpMessages();
                }
            }

            // Pumps the pending Win32 messages of this apartment.
            private static void PumpMessages()
            {
                while (PeekMessage(out MSG m, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref m);
                    DispatchMessage(ref m);
                }
            }

            // True if the worker was retired or has been inside OLE longer than GraceMs.
            public bool IsStuck()
            {
                if (Retired) return true;
                long since = Volatile.Read(ref _busySince);
                if (since == 0) return false;
                return (DateTime.UtcNow - new DateTime(since, DateTimeKind.Utc)).TotalMilliseconds > GraceMs;
            }
        }

        // One clipboard job together with its TaskCompletionSource.
        private readonly struct Job
        {
            public const int Batch = 8;
            public readonly Func<(bool ok, string? text)> Work;
            public readonly TaskCompletionSource<(bool, string?)> Tcs;
            public Job(Func<(bool, string? text)> work, TaskCompletionSource<(bool, string?)> tcs) { Work = work; Tcs = tcs; }
        }

        private static Worker Acquire()
        {
            lock (Gate)
            {
                if (_current != null && !_current.IsStuck()) return _current;
                if (_current != null) _current.Retired = true;
                if (_spawned >= MaxWorkers) return _current!;      // stop instead of leaking threads
                _spawned++;
                _current = new Worker();
                return _current;
            }
        }
        // Runs a clipboard operation on an STA worker; it never throws and never blocks.
        private static async Task<(bool ok, string? text)> RunAsync(Func<(bool, string? text)> work, int timeoutMs)
        {
            Task<(bool, string?)> task = Acquire().Submit(work);
            var done = await Task.WhenAny(task, Task.Delay(timeoutMs)).ConfigureAwait(true);
            return done == task && task.IsCompletedSuccessfully ? task.Result : (false, null);
        }

        private const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x0002;
        private const int OpenTries = 20, OpenPauseMs = 15;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr handle);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalFree(IntPtr handle);

        // Opens the clipboard, retrying while another process still owns it.
        private static bool OpenWithRetry()
        {
            for (int i = 0; i < OpenTries; i++)
            {
                if (OpenClipboard(IntPtr.Zero)) return true;
                Thread.Sleep(OpenPauseMs);
            }
            return false;
        }

        // Replaces the clipboard text; false only when the clipboard stays locked.
        private static bool SetClipboardTextRaw(string text)
        {
            if (!OpenWithRetry()) return false;
            IntPtr block = IntPtr.Zero;
            try
            {
                if (!EmptyClipboard()) return false;
                byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
                block = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(uint)bytes.Length);
                if (block == IntPtr.Zero) return false;
                IntPtr target = GlobalLock(block);
                if (target == IntPtr.Zero) return false;
                try { Marshal.Copy(bytes, 0, target, bytes.Length); }
                finally { GlobalUnlock(block); }
                if (SetClipboardData(CF_UNICODETEXT, block) == IntPtr.Zero) return false;
                block = IntPtr.Zero;          // the clipboard now owns this memory
                return true;
            }
            finally
            {
                if (block != IntPtr.Zero) GlobalFree(block);
                CloseClipboard();
            }
        }

        // Reads the raw clipboard text as UTF-16, or null when it cannot be opened or holds no text.
        private static string? GetClipboardTextRaw()
        {
            if (!OpenWithRetry()) return null;
            try
            {
                if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
                IntPtr handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero) return null;
                IntPtr src = GlobalLock(handle);
                if (src == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(src); }
                finally { GlobalUnlock(handle); }
            }
            finally { CloseClipboard(); }
        }

        // Reads clipboard text through the STA worker with a timeout; null on failure or timeout.
        public static async Task<string?> GetTextAsync(int timeoutMs = 900)
        {
            var (ok, text) = await RunAsync(() => { try { return (true, GetClipboardTextRaw()); } catch { return (false, null); } }, timeoutMs);
            return ok ? text : null;
        }

        // Writes clipboard text through the STA worker with a timeout; false on failure or timeout.
        public static async Task<bool> SetTextAsync(string text, int timeoutMs = 900)
        {
            var (ok, _) = await RunAsync(() => { try { return (SetClipboardTextRaw(text), (string?)null); } catch { return (false, null); } }, timeoutMs);
            return ok;
        }

        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();

        public static uint SequenceNumber() => GetClipboardSequenceNumber();
    }
}
