using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MagicSelect
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(32, 33, 36);
        public static readonly Color Surface = Color.FromArgb(48, 49, 52);
        public static readonly Color SurfaceHover = Color.FromArgb(64, 66, 70);
        public static readonly Color Border = Color.FromArgb(70, 72, 77);
        public static readonly Color Text = Color.FromArgb(232, 234, 237);
        public static readonly Color Muted = Color.FromArgb(154, 160, 166);
        public static readonly Color Accent = Color.FromArgb(138, 180, 248);
        public static readonly Color Error = Color.FromArgb(242, 139, 130);
        public static readonly Color Success = Color.FromArgb(129, 201, 149);
        public static readonly Color Card = Color.FromArgb(40, 41, 45);

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>Translucent rectangle that follows the right-button drag. Click-through.</summary>
    class SelectionBox : Form
    {
        public SelectionBox()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Accent;
            Opacity = 0.30;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            var h = Handle; // create now so the first drag is instant
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW |
                              Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void ShowRect(Rectangle r)
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height),
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public void HideBox() { Native.ShowWindow(Handle, 0); }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var pen = new Pen(Color.White, 3))
                e.Graphics.DrawRectangle(pen, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
        }
    }

    /// <summary>Rounded "chip" button with an optional icon glyph.</summary>
    class PillButton : Control
    {
        public string Icon;
        public Font IconFont;
        public bool Primary;
        public bool Outline = false;   // subtle accent-bordered chip (smart actions)
        bool hover;

        public PillButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            ForeColor = Theme.Text;
        }

        public Size Measure(int padX, int gap)
        {
            Size t = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            int w = t.Width + padX * 2;
            if (!string.IsNullOrEmpty(Icon) && IconFont != null)
            {
                Size i = TextRenderer.MeasureText(Icon, IconFont, Size.Empty, TextFormatFlags.NoPadding);
                w += i.Width + (string.IsNullOrEmpty(Text) ? 0 : gap);
            }
            return new Size(w, 0);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Color fill = Primary ? Theme.Accent : (hover && Enabled ? Theme.SurfaceHover : (Outline ? Theme.Bg : Theme.Surface));
            Color fore = Primary ? Theme.Bg : (Outline ? Theme.Accent : (Enabled ? ForeColor : Theme.Muted));
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), (Height - 1) / 2f))
            using (var b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
                if (Outline)
                    using (var p = new Pen(Color.FromArgb(110, Theme.Accent), 1f)) g.DrawPath(p, path);
            }

            int gap = Height / 4;
            Size ts = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            Size isz = (!string.IsNullOrEmpty(Icon) && IconFont != null)
                ? TextRenderer.MeasureText(Icon, IconFont, Size.Empty, TextFormatFlags.NoPadding) : Size.Empty;
            int total = ts.Width + isz.Width + (isz.Width > 0 && ts.Width > 0 ? gap : 0);
            int x = (Width - total) / 2;
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            if (isz.Width > 0)
            {
                TextRenderer.DrawText(g, Icon, IconFont, new Rectangle(x, 0, isz.Width, Height), Primary ? fore : Theme.Accent, flags);
                x += isz.Width + gap;
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, ts.Width + 2, Height), fore, flags);
        }
    }

    /// <summary>Base for the app's windows: dark theme, dark title bar, DPI-aware sizing helper.</summary>
    class DarkWindow : Form
    {
        protected readonly float s;
        protected readonly Font font, smallFont, boldFont, titleFont, iconFont;

        protected int S(float v) { return (int)Math.Round(v * s); }

        public DarkWindow()
        {
            s = Native.ScaleAt(Cursor.Position);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            font = new Font("Segoe UI", S(14), GraphicsUnit.Pixel);
            smallFont = new Font("Segoe UI", S(12.5f), GraphicsUnit.Pixel);
            boldFont = new Font("Segoe UI Semibold", S(15), GraphicsUnit.Pixel);
            titleFont = new Font("Segoe UI Semibold", S(22), GraphicsUnit.Pixel);
            iconFont = new Font("Segoe MDL2 Assets", S(13), GraphicsUnit.Pixel);
            Font = font;
            try { Icon = Brand.GetIcon(32); } catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            Native.DwmSetWindowAttribute(Handle, 20, ref on, 4); // dark title bar
            Native.RoundCorners(Handle, Theme.Border);
        }

        /// <summary>Centers the window on the monitor under the mouse.</summary>
        protected void CenterOnScreen()
        {
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        }

        protected Label AddLabel(Control parent, string text, Font f, Color color, int x, int y, int width)
        {
            var l = new Label { Text = text, Font = f, ForeColor = color, BackColor = Color.Transparent, AutoSize = false };
            // Measure a bit narrower than the label so the wrap never cuts the last word.
            Size sz = TextRenderer.MeasureText(text, f, new Size(width - S(40), 10000), TextFormatFlags.WordBreak);
            l.SetBounds(x, y, width, sz.Height + S(2));
            parent.Controls.Add(l);
            return l;
        }

        protected PillButton MakeButton(string text, string icon, bool primary)
        {
            var b = new PillButton { Text = text, Icon = icon, IconFont = iconFont, Font = font, Primary = primary };
            return b;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { font.Dispose(); smallFont.Dispose(); boldFont.Dispose(); titleFont.Dispose(); iconFont.Dispose(); }
        }
    }

    /// <summary>Dark look for the tray menu.</summary>
    class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            TextRenderer.DrawText(e.Graphics, "", new Font("Segoe MDL2 Assets", 9f), r, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Theme.SurfaceHover; } }
            public override Color MenuItemSelected { get { return Theme.SurfaceHover; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Card; } }
        }
    }

    /// <summary>Rounded "card" section background.</summary>
    class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            using (var path = Theme.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 12))
            using (var b = new SolidBrush(Theme.Card))
                g.FillPath(b, path);
        }
    }

    /// <summary>iOS/Windows-11 style on/off switch.</summary>
    class ToggleSwitch : Control
    {
        bool on;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Theme.RoundRect(r, r.Height / 2))
            {
                using (var b = new SolidBrush(on ? Theme.Accent : Theme.Surface)) g.FillPath(b, path);
                if (!on) using (var p = new Pen(Theme.Muted, 1.2f)) g.DrawPath(p, path);
            }
            float d = r.Height - 8;
            float x = on ? r.Right - d - 4 : r.X + 4;
            using (var b = new SolidBrush(on ? Theme.Bg : Theme.Muted)) g.FillEllipse(b, x, r.Y + 4, d, d);
        }
    }

    /// <summary>Dark rounded text field.</summary>
    class TextField : InputShell
    {
        public readonly TextBox Box;

        public TextField(Font f, bool password)
        {
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = f, UseSystemPasswordChar = password };
            Box.GotFocus += delegate { Focused2 = true; Invalidate(); };
            Box.LostFocus += delegate { Focused2 = false; Invalidate(); };
            Controls.Add(Box);
            Click += delegate { Box.Focus(); };
        }

        public string Value { get { return Box.Text; } set { Box.Text = value; } }

        public void SetPlaceholder(string text)
        {
            if (Box.IsHandleCreated) Native.SendMessage(Box.Handle, 0x1501, new IntPtr(1), text);
            else Box.HandleCreated += delegate { Native.SendMessage(Box.Handle, 0x1501, new IntPtr(1), text); };
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Height / 3;
            int th = Box.PreferredHeight;
            Box.SetBounds(pad, (Height - th) / 2, Width - pad * 2 - ReservedRight, th);
        }

        public int ReservedRight = 0;
    }

    /// <summary>Rounded container that hosts the question TextBox.</summary>
    class InputShell : Panel
    {
        public bool Focused2;

        public InputShell()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            using (var path = Theme.RoundRect(new RectangleF(1, 1, Width - 2.5f, Height - 2.5f), Math.Min(12, (Height - 2) / 2f)))
            {
                using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (var p = new Pen(Focused2 ? Theme.Accent : Theme.Border, 1.5f)) g.DrawPath(p, path);
            }
        }
    }
}
