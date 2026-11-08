using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MagicSelect
{
    /// <summary>Popup with everything you copied: search, click to copy again, delete.</summary>
    class HistoryForm : Form
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        readonly ClipboardHistory history;
        readonly float s;
        readonly Font font, smallFont, boldFont, iconFont;
        readonly ListBox list;
        readonly TextField search;
        readonly Label empty;
        readonly List<ClipItem> shown = new List<ClipItem>();
        int hover = -1, copied = -1;
        bool asking;
        readonly Timer closeTimer = new Timer();

        int S(float v) { return (int)Math.Round(v * s); }

        public HistoryForm(ClipboardHistory history, Point anchor)
        {
            this.history = history;
            s = Native.ScaleAt(anchor);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            KeyPreview = true;
            Text = "Histórico de cópias";

            font = new Font("Segoe UI", S(14), GraphicsUnit.Pixel);
            smallFont = new Font("Segoe UI", S(12), GraphicsUnit.Pixel);
            boldFont = new Font("Segoe UI Semibold", S(16), GraphicsUnit.Pixel);
            iconFont = new Font("Segoe MDL2 Assets", S(12), GraphicsUnit.Pixel);

            int W = S(420), H = S(540), pad = S(14);
            ClientSize = new Size(W, H);

            var title = new Label { Text = "Histórico de cópias", Font = boldFont, ForeColor = Theme.Text, BackColor = Theme.Bg, AutoSize = true };
            title.Location = new Point(pad, S(14));
            Controls.Add(title);

            var clear = new PillButton { Text = "Limpar tudo", Icon = "", IconFont = iconFont, Font = smallFont };
            int cw = clear.Measure(S(12), S(6)).Width;
            clear.SetBounds(W - pad - cw, S(12), cw, S(28));
            clear.Click += delegate { ClearAll(); };
            Controls.Add(clear);

            search = new TextField(font, false);
            search.SetPlaceholder("Buscar no que você copiou…");
            search.SetBounds(pad, S(50), W - pad * 2, S(38));
            search.Box.TextChanged += delegate { Refill(); };
            search.Box.KeyDown += OnSearchKey;
            Controls.Add(search);

            list = new ListBox
            {
                DrawMode = DrawMode.OwnerDrawVariable,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Bg,
                ForeColor = Theme.Text,
                IntegralHeight = false,
                Font = font
            };
            list.SetBounds(pad - S(4), S(98), W - pad * 2 + S(8), H - S(98) - S(10));
            list.MeasureItem += OnMeasure;
            list.DrawItem += OnDraw;
            list.MouseMove += OnListMouseMove;
            list.MouseLeave += delegate { SetHover(-1); };
            list.MouseUp += OnListClick;
            list.HandleCreated += delegate { SetWindowTheme(list.Handle, "DarkMode_Explorer", null); }; // dark scrollbar
            Controls.Add(list);

            empty = new Label { ForeColor = Theme.Muted, BackColor = Theme.Bg, Font = font, TextAlign = ContentAlignment.MiddleCenter, Visible = false };
            empty.SetBounds(pad, S(160), W - pad * 2, S(120));
            Controls.Add(empty);
            empty.BringToFront();

            closeTimer.Interval = 450;
            closeTimer.Tick += delegate { closeTimer.Stop(); Close(); };

            history.Changed += OnHistoryChanged;
            Refill();

            // Place at the anchor, kept fully on screen.
            Rectangle wa = Screen.FromPoint(anchor).WorkingArea;
            int x = Math.Max(wa.Left + S(8), Math.Min(anchor.X, wa.Right - W - S(8)));
            int y = Math.Max(wa.Top + S(8), Math.Min(anchor.Y, wa.Bottom - H - S(8)));
            Location = new Point(x, y);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= Native.CS_DROPSHADOW;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle, Theme.Border);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            search.Box.Focus();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!closeTimer.Enabled && !asking) Close(); // click outside closes it
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            base.OnKeyDown(e);
        }

        void OnSearchKey(object sender, KeyEventArgs e)
        {
            // Enter copies the first result; arrows move into the list.
            if (e.KeyCode == Keys.Enter && shown.Count > 0) { e.SuppressKeyPress = true; CopyAt(Math.Max(0, list.SelectedIndex)); }
            else if (e.KeyCode == Keys.Down && shown.Count > 0) { e.Handled = true; list.Focus(); list.SelectedIndex = 0; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (list.Focused && keyData == Keys.Enter && list.SelectedIndex >= 0) { CopyAt(list.SelectedIndex); return true; }
            if (list.Focused && keyData == Keys.Delete && list.SelectedIndex >= 0) { DeleteAt(list.SelectedIndex); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void OnHistoryChanged(object sender, EventArgs e)
        {
            if (!IsDisposed && !closeTimer.Enabled) Refill();
        }

        void Refill()
        {
            string q = search.Value.Trim();
            shown.Clear();
            foreach (var it in history.Items)
            {
                if (q.Length == 0 || (it.Kind != ClipKind.Image && it.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                    shown.Add(it);
            }
            list.BeginUpdate();
            list.Items.Clear();
            for (int i = 0; i < shown.Count; i++) list.Items.Add(i);
            list.EndUpdate();
            hover = -1;

            empty.Visible = shown.Count == 0;
            empty.Text = q.Length > 0
                ? "Nada encontrado para \"" + q + "\"."
                : "Nada copiado ainda.\nTudo que você copiar (Ctrl+C) em qualquer programa aparece aqui.";
        }

        // ---------- drawing ----------

        void OnMeasure(object sender, MeasureItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= shown.Count) return;
            e.ItemHeight = shown[e.Index].Kind == ClipKind.Image ? S(112) : S(64);
        }

        void OnDraw(object sender, DrawItemEventArgs e)
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(Theme.Bg)) g.FillRectangle(bg, e.Bounds);
            if (e.Index < 0 || e.Index >= shown.Count) return;
            var it = shown[e.Index];
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var card = new Rectangle(e.Bounds.X + S(4), e.Bounds.Y + S(3), e.Bounds.Width - S(8), e.Bounds.Height - S(6));
            bool hot = e.Index == hover || (e.State & DrawItemState.Selected) != 0;
            using (var path = Theme.RoundRect(card, S(10)))
            using (var b = new SolidBrush(e.Index == copied ? Color.FromArgb(40, 70, 52) : (hot ? Theme.SurfaceHover : Theme.Card)))
                g.FillPath(b, path);

            int pad = S(12), rightCol = S(70);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

            // time / status on the right
            string when = e.Index == copied ? "Copiado ✓" : Ago(it.Time);
            TextRenderer.DrawText(g, when, smallFont, new Rectangle(card.Right - rightCol - pad, card.Y + S(9), rightCol, S(18)),
                e.Index == copied ? Theme.Success : Theme.Muted, flags | TextFormatFlags.Right);
            if (hot && e.Index != copied)
                TextRenderer.DrawText(g, "", iconFont, new Rectangle(card.Right - S(34), card.Bottom - S(30), S(24), S(24)),
                    Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            var content = new Rectangle(card.X + pad, card.Y + S(8), card.Width - pad * 2 - rightCol - S(8), card.Height - S(16));
            if (it.Kind == ClipKind.Image && it.Thumb != null)
            {
                float k = Math.Min((float)content.Height / it.Thumb.Height, (float)content.Width / it.Thumb.Width);
                k = Math.Min(k, 1f);
                var dst = new Rectangle(content.X, content.Y, (int)(it.Thumb.Width * k), (int)(it.Thumb.Height * k));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(it.Thumb, dst);
                using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, dst);
                TextRenderer.DrawText(g, "Imagem  " + it.Text, smallFont,
                    new Rectangle(card.Right - rightCol - pad - S(40), card.Y + S(30), rightCol + S(40), S(18)), Theme.Muted, flags | TextFormatFlags.Right);
            }
            else
            {
                string text = it.Text.Replace("\r", "").Trim();
                if (it.Kind == ClipKind.Files)
                {
                    text = it.Files.Length == 1 ? System.IO.Path.GetFileName(it.Files[0])
                                                : it.Files.Length + " arquivos: " + string.Join(", ", Array.ConvertAll(it.Files, f => System.IO.Path.GetFileName(f)));
                    TextRenderer.DrawText(g, "", iconFont, new Rectangle(content.X, content.Y + S(2), S(18), S(18)), Theme.Accent, flags);
                    content = new Rectangle(content.X + S(22), content.Y, content.Width - S(22), content.Height);
                }
                // collapse runs of blank lines so the 2-line preview shows real content
                while (text.Contains("\n\n")) text = text.Replace("\n\n", "\n");
                TextRenderer.DrawText(g, text, font, content, Theme.Text,
                    TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            }
        }

        static string Ago(DateTime t)
        {
            TimeSpan d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "agora";
            if (d.TotalMinutes < 60) return "há " + (int)d.TotalMinutes + " min";
            if (d.TotalHours < 24 && t.Date == DateTime.Today) return "há " + (int)d.TotalHours + " h";
            if (t.Date == DateTime.Today.AddDays(-1)) return "ontem";
            return t.ToString("dd/MM");
        }

        // ---------- mouse ----------

        void OnListMouseMove(object sender, MouseEventArgs e)
        {
            int i = list.IndexFromPoint(e.Location);
            if (i >= shown.Count) i = -1;
            if (i >= 0 && !list.GetItemRectangle(i).Contains(e.Location)) i = -1;
            SetHover(i);
            list.Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        }

        void SetHover(int i)
        {
            if (i == hover) return;
            int old = hover;
            hover = i;
            if (old >= 0 && old < list.Items.Count) list.Invalidate(list.GetItemRectangle(old));
            if (i >= 0) list.Invalidate(list.GetItemRectangle(i));
        }

        void OnListClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int i = list.IndexFromPoint(e.Location);
            if (i < 0 || i >= shown.Count || !list.GetItemRectangle(i).Contains(e.Location)) return;
            Rectangle r = list.GetItemRectangle(i);
            bool trash = e.X >= r.Right - S(44) && e.Y >= r.Bottom - S(36);
            if (trash) DeleteAt(i);
            else CopyAt(i);
        }

        void CopyAt(int i)
        {
            if (i < 0 || i >= shown.Count) return;
            if (!history.CopyBack(shown[i])) return;
            copied = i;
            list.Invalidate();
            closeTimer.Start(); // show "Copiado ✓" briefly, then close
        }

        void DeleteAt(int i)
        {
            if (i < 0 || i >= shown.Count) return;
            history.Remove(shown[i]);
        }

        void ClearAll()
        {
            if (history.Items.Count == 0) return;
            asking = true; // the confirmation box takes focus; don't treat that as "clicked outside"
            try
            {
                if (MessageBox.Show(this, "Apagar todo o histórico de cópias?", Brand.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    history.Clear();
            }
            finally { asking = false; }
            Activate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                history.Changed -= OnHistoryChanged;
                closeTimer.Dispose();
            }
            base.Dispose(disposing);
            if (disposing) { font.Dispose(); smallFont.Dispose(); boldFont.Dispose(); iconFont.Dispose(); }
        }
    }
}
