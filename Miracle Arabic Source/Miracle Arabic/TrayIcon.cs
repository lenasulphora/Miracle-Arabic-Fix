using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Miracle
{
    // Gives a Win32 window rounded corners through a Region; WinForms menus have no rounding.
    internal static class MenuWindowShape
    {
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

        // Clips the window to a rounded rectangle with the given radius; bad values are ignored.
        internal static void Round(IntPtr hWnd, int width, int height, int radius)
        {
            if (hWnd == IntPtr.Zero || width <= 0 || height <= 0) return;
            IntPtr rgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
            if (rgn == IntPtr.Zero) return;
            SetWindowRgn(hWnd, rgn, true);
        }
    }

    internal static class TrayDispatch
    {
        private static Action<Action>? _invoke;

        // Registers the UI-thread dispatch function.
        internal static void Register(Action<Action> invoke) => _invoke = invoke;

        // Runs an action on the UI thread when possible; null actions are ignored.
        internal static void Invoke(Action action)
        {
            if (action is null) return;
            var inv = _invoke;
            if (inv is null) action();
            else inv(action);
        }
    }

    // State of the options that the tray menu can toggle.
    public sealed class TrayFlags
    {
        public bool Mini;
        public bool Translate;
        public bool Easy;
        public bool Correct;
        public bool Transparent;
    }

    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly ToolStripMenuItem _toggleItem = null!;
        private readonly ToolStripMenuItem _miniItem = null!;
        private readonly ToolStripMenuItem _translateItem = null!;
        private readonly ToolStripMenuItem _easyItem = null!;
        private readonly ToolStripMenuItem _correctItem = null!;
        private readonly TrayFlags _flags;
        private Icon? _ownedIcon;

        public Action? OnToggle { get; set; }
        public Action? OnExit { get; set; }

        public Action<TrayFlags>? OnFlagsChanged { get; set; }

        public TrayIcon(string tooltip, TrayFlags flags)
        {
            _flags = flags;
            try { _ownedIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
            _icon = new NotifyIcon
            {
                Icon = _ownedIcon ?? SystemIcons.Application,
                Visible = true,
                Text = tooltip
            };

            _toggleItem = new ToolStripMenuItem("Show tool", null, (s, e) => OnToggle?.Invoke());
            _miniItem = Checkable("Mini mode", flags.Mini);
            _translateItem = Checkable("Translate", flags.Translate);
            _easyItem = Checkable("EASY", flags.Easy);
            _correctItem = Checkable("CORRECT", flags.Correct);
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => OnExit?.Invoke());

            var menu = new ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                BackColor = DarkMenuColorTable.Surface,
                ForeColor = DarkMenuColorTable.Text,
                ShowCheckMargin = true,
                ShowImageMargin = false,
                DropShadowEnabled = true,
                Padding = new Padding(0, 4, 0, 4)
            };
            menu.Items.AddRange(new ToolStripItem[]
            {
                _toggleItem,
                new ToolStripSeparator(),
                _miniItem,
                _translateItem,
                _easyItem,
                _correctItem,
                new ToolStripSeparator(),
                exitItem
            });
            foreach (ToolStripItem item in menu.Items) item.Padding = new Padding(0, 3, 10, 3);
            menu.HandleCreated += (s, e) => RoundMenu(menu);
            menu.Resize += (s, e) => RoundMenu(menu);
            menu.Opening += (s, e) => BeginInvoke(RoundMenu, menu);
            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (s, e) => OnToggle?.Invoke();
        }

        // Runs the action once the menu handle exists, because rounding needs a real handle.
        private static void BeginInvoke(Action<ContextMenuStrip> action, ContextMenuStrip menu)
        {
            if (menu.IsHandleCreated) action(menu);
            else menu.HandleCreated += (s, e) => action(menu);
        }

        // Rounds the menu border; called on creation and on every resize.
        private static void RoundMenu(ContextMenuStrip menu)
        {
            if (!menu.IsHandleCreated) return;
            MenuWindowShape.Round(menu.Handle, menu.Width, menu.Height, DarkMenuRenderer.CornerRadius);
        }

        // Creates a checkable menu item; every click raises the flags-changed notification.
        private ToolStripMenuItem Checkable(string text, bool initial)
        {
            return new ToolStripMenuItem(text, null, (s, e) => RaiseFlagsChanged())
            {
                CheckOnClick = true,
                AutoSize = true,
                Checked = initial
            };
        }

        // Reads the option states from the menu items and reports them to the main window.
        private void RaiseFlagsChanged()
        {
            _flags.Mini = _miniItem.Checked;
            _flags.Translate = _translateItem.Checked;
            _flags.Easy = _easyItem.Checked;
            _flags.Correct = _correctItem.Checked;
            OnFlagsChanged?.Invoke(_flags);
        }

        // Updates the show/hide item label to match the current window state.
        public void SetShownLabel(bool shown) => _toggleItem.Text = shown ? "Hide tool" : "Show tool";

        public void SyncChecks(bool mini, bool translate, bool easy, bool correct, bool transparent)
        {
            _flags.Mini = mini; _flags.Translate = translate; _flags.Easy = easy;
            _flags.Correct = correct; _flags.Transparent = transparent;
            _miniItem.Checked = mini;
            _translateItem.Checked = translate;
            _easyItem.Checked = easy;
            _correctItem.Checked = correct;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _ownedIcon?.Dispose();
            _ownedIcon = null;
        }
    }
}
