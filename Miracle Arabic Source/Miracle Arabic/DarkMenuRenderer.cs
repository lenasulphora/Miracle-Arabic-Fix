using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Miracle
{
    // Dark theme colours for the tray menus (surface, border, hover, accent, text).
    internal sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        internal static readonly Color Surface = Color.FromArgb(0x10, 0x10, 0x13);
        internal static readonly Color Line = Color.FromArgb(0x2A, 0x2A, 0x32);
        internal static readonly Color Hover = Color.FromArgb(0x2C, 0x2C, 0x34);
        internal static readonly Color Accent = Color.FromArgb(0xA8, 0x55, 0xF7);
        internal static readonly Color Text = Color.FromArgb(0xF4, 0xF4, 0xF5);
        internal static readonly Color TextDisabled = Color.FromArgb(0x6B, 0x6B, 0x74);

        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Line;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientMiddle => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        // Corner radius of the menu, also used by MenuWindowShape.
        internal const int CornerRadius = 8;

        public DarkMenuRenderer() : base(new DarkMenuColorTable())
        {
        }

        // Fills the menu background with the flat surface colour instead of a gradient.
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(DarkMenuColorTable.Surface);
        }

        // Paints over the empty image margin.
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using var b = new SolidBrush(DarkMenuColorTable.Surface);
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        // Draws a rounded menu border instead of the default square one.
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            var save = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using var path = Rounded(r, 8);
            using var p = new Pen(DarkMenuColorTable.Line);
            g.DrawPath(p, path);
            g.Restore(save);
        }

        // Item text colour for enabled and disabled items.
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? DarkMenuColorTable.Text : DarkMenuColorTable.TextDisabled;
            base.OnRenderItemText(e);
        }

        // Suppresses the default check mark, which DrawMark draws instead.
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
        }

        // Routes every item background through the menu-item path so all items look the same.
        protected override void OnRenderItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is ToolStripMenuItem)
            {
                OnRenderMenuItemBackground(e);
                return;
            }
            base.OnRenderItemBackground(e);
        }

        // Item background: a hover-coloured pill when selected or pressed, surface colour otherwise.
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is not ToolStripMenuItem mi)
            {
                base.OnRenderMenuItemBackground(e);
                return;
            }

            var local = new Rectangle(Point.Empty, mi.Size);

            if (mi.Selected || mi.Pressed)
            {
                var pill = Rectangle.Inflate(local, 1, 0);
                using var b = new SolidBrush(DarkMenuColorTable.Hover);
                using var path = Rounded(pill, 6);
                var gs = e.Graphics.Save();
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(b, path);
                e.Graphics.Restore(gs);
            }
            else
            {
                using var b = new SolidBrush(DarkMenuColorTable.Surface);
                e.Graphics.FillRectangle(b, local);
            }

            if (mi.CheckOnClick)
                DrawMark(e.Graphics, mi);
        }

        // Draws the check mark: an accent tick when on, an empty circle when off.
        private static void DrawMark(Graphics g, ToolStripMenuItem mi)
        {
            const int MarkSize = 14;
            const int MarkInset = 10;
            int top = (mi.Height - MarkSize) / 2;
            var r = new Rectangle(MarkInset, top, MarkSize, MarkSize);
            bool on = mi.CheckState != CheckState.Unchecked;

            var offColor = mi.Selected
                ? Color.FromArgb(0x8B, 0x8B, 0x96)
                : DarkMenuColorTable.TextDisabled;

            var save = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (on)
            {
                using var pen = new Pen(DarkMenuColorTable.Accent, 2.2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };
                float x = r.Left, y = r.Top, w = r.Width, h = r.Height;
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * 0.16f, y + h * 0.52f),
                    new PointF(x + w * 0.40f, y + h * 0.78f),
                    new PointF(x + w * 0.86f, y + h * 0.22f)
                });
            }
            else
            {
                using var pen = new Pen(offColor, 1.4f);
                g.DrawEllipse(pen, r.X + 2, r.Y + 2, r.Width - 5, r.Height - 5);
            }
            g.Restore(save);
        }

        internal static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (r.Width <= d || r.Height <= d)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d - 1, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
