using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Miracle
{
    internal sealed class ClipboardMonitor : IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private HwndSource? _source;
        private bool _listening;

        // Raised on the UI thread for every clipboard change; never throws.
        public event Action? ClipboardUpdated;

        public bool Start()
        {
            if (_source != null) return _listening;

            try
            {
                var parameters = new HwndSourceParameters("MiracleClipboardMonitor")
                {
                    WindowStyle = 0,                    // not WS_VISIBLE, so it stays hidden
                    ExtendedWindowStyle = WS_EX_TOOLWINDOW,
                    Width = 0,
                    Height = 0,
                };

                _source = new HwndSource(parameters);
                _source.AddHook(WndProc);
                _listening = AddClipboardFormatListener(_source.Handle);

                if (!_listening)
                {
                    Dispose();
                    return false;
                }

                return true;
            }
            catch
            {
                Dispose();
                return false;
            }
        }

        // Window procedure of the hidden window: raises the event on a clipboard change.
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                // A subscriber must never crash the window procedure.
                try { ClipboardUpdated?.Invoke(); } catch { }
            }

            return IntPtr.Zero;
        }

        // Unregisters and destroys the listener window, ignoring errors during shutdown.
        public void Dispose()
        {
            if (_source == null) return;

            try
            {
                if (_listening) RemoveClipboardFormatListener(_source.Handle);
            }
            catch { }
            _listening = false;

            try { _source.RemoveHook(WndProc); } catch { }
            try { _source.Dispose(); } catch { }
            _source = null;
        }
    }
}
