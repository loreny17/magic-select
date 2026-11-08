using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MagicSelect
{
    /// <summary>The floating card shown on top of a selection: actions, question box and streamed answer.</summary>
    partial class ActionCard : Form
    {
        class Item
        {
            public bool IsUser;
            public bool IsError;
            public string Text = "";
        }

        readonly App app;
        readonly Config cfg;
        readonly Rectangle selection;
        readonly string imageB64;
        readonly Bitmap shot; // the selection exactly as it was on screen
        readonly float s;

        readonly Font font, smallFont, iconFont, boldFont, italicFont;
        readonly List<PillButton> actions = new List<PillButton>();
        readonly List<PillButton> tools = new List<PillButton>();   // icon-only, right side
        readonly List<PillButton> smart = new List<PillButton>();   // smart actions, only when found
        readonly ToolTip tips = new ToolTip();
        readonly PillButton speakBtn;
        System.Speech.Synthesis.SpeechSynthesizer synth;
        readonly InputShell inputShell;
        readonly TextBox input;
        readonly PillButton sendBtn;
        readonly RichTextBox answer;
        readonly PillButton copyBtn, keyBtn, folderBtn;
        string lastPrintFolder;
        readonly Label status;

        readonly List<Turn> history = new List<Turn>();
        readonly List<Item> display = new List<Item>();
        string lastAnswer = "";
        bool busy;
        int requestId;
        bool answerVisible;
        int answerHeight;

        int S(float v) { return (int)Math.Round(v * s); }

        public ActionCard(App app, Config cfg, Rectangle selection, Bitmap image)
        {
            this.app = app;
            this.cfg = cfg;
            this.selection = selection;
            imageB64 = EncodeImage(image);
            shot = new Bitmap(image);
            s = Native.ScaleAt(new Point(selection.Left + selection.Width / 2, selection.Top));

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            KeyPreview = true;
            Text = "Magic Select";

            font = new Font("Segoe UI", S(14), GraphicsUnit.Pixel);
            smallFont = new Font("Segoe UI", S(12), GraphicsUnit.Pixel);
            boldFont = new Font("Segoe UI", S(14), FontStyle.Bold, GraphicsUnit.Pixel);
            italicFont = new Font("Segoe UI", S(13), FontStyle.Italic, GraphicsUnit.Pixel);
            iconFont = new Font("Segoe MDL2 Assets", S(13), GraphicsUnit.Pixel);

            AddAction("", "Traduzir", Translate);
            AddAction("", "Pesquisar", Search);
            AddAction("", "Explicar", Explain);
            AddAction("", "Copiar texto", CopyText, true);
            AddAction("", "Print (copiar imagem)", TakePrint, true);

            speakBtn = AddAction(IconSpeak, "Ler em voz alta", ReadAloud, true);
            AddAction("", "Histórico de cópias", OpenHistory, true);

            inputShell = new InputShell();
            input = new TextBox();
            input.BorderStyle = BorderStyle.None;
            input.BackColor = Theme.Surface;
            input.ForeColor = Theme.Text;
            input.Font = font;
            input.KeyDown += OnInputKey;
            input.GotFocus += delegate { inputShell.Focused2 = true; inputShell.Invalidate(); };
            input.LostFocus += delegate { inputShell.Focused2 = false; inputShell.Invalidate(); };
            inputShell.Controls.Add(input);
            inputShell.Click += delegate { input.Focus(); };
            Controls.Add(inputShell);

            sendBtn = new PillButton();
            sendBtn.Icon = "";
            sendBtn.IconFont = iconFont;
            sendBtn.Primary = true;
            sendBtn.Font = font;
            sendBtn.Click += delegate { AskTyped(); };
            inputShell.Controls.Add(sendBtn);

            answer = new RichTextBox();
            answer.BorderStyle = BorderStyle.None;
            answer.BackColor = Theme.Bg;
            answer.ForeColor = Theme.Text;
            answer.Font = font;
            answer.ReadOnly = true;
            answer.ScrollBars = RichTextBoxScrollBars.Vertical;
            answer.DetectUrls = true;
            answer.LinkClicked += delegate(object o, LinkClickedEventArgs e) { OpenUrl(e.LinkText); };
            answer.Visible = false;
            Controls.Add(answer);

            copyBtn = new PillButton();
            copyBtn.Icon = "";
            copyBtn.IconFont = iconFont;
            copyBtn.Text = "Copiar resposta";
            copyBtn.Font = smallFont;
            copyBtn.Visible = false;
            copyBtn.Click += delegate
            {
                if (lastAnswer.Length > 0) status.Text = SetClipboard(lastAnswer) ? "Copiado ✓" : "Não consegui copiar";
            };
            Controls.Add(copyBtn);

            keyBtn = new PillButton();
            keyBtn.Icon = "";
            keyBtn.IconFont = iconFont;
            keyBtn.Text = "Abrir configurações";
            keyBtn.Font = smallFont;
            keyBtn.Primary = true;
            keyBtn.Visible = false;
            keyBtn.Click += delegate { Close(); app.ShowSettings(); };
            Controls.Add(keyBtn);

            folderBtn = new PillButton();
            folderBtn.Icon = "";
            folderBtn.IconFont = iconFont;
            folderBtn.Text = "Abrir pasta";
            folderBtn.Font = smallFont;
            folderBtn.Visible = false;
            folderBtn.Click += delegate { if (lastPrintFolder != null) OpenUrl(lastPrintFolder); };
            Controls.Add(folderBtn);

            status = new Label();
            status.AutoSize = false;
            status.ForeColor = Theme.Muted;
            status.Font = smallFont;
            status.TextAlign = ContentAlignment.MiddleRight;
            status.BackColor = Theme.Bg;
            Controls.Add(status);

            DoLayout();
        }

        /// <summary>iconOnly = small round button on the right with the name as a tooltip (keeps the card clean).</summary>
        PillButton AddAction(string icon, string text, MethodInvoker onClick, bool iconOnly = false)
        {
            var b = new PillButton();
            b.Icon = icon;
            b.IconFont = iconFont;
            b.Font = font;
            b.Click += delegate { if (!busy || b == speakBtn) onClick(); };
            if (iconOnly)
            {
                tips.SetToolTip(b, text);
                tools.Add(b);
            }
            else
            {
                b.Text = text;
                actions.Add(b);
            }
            Controls.Add(b);
            return b;
        }

        const string IconSpeak = "", IconStop = "";

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
            const int EM_SETCUEBANNER = 0x1501;
            Native.SendMessage(input.Handle, EM_SETCUEBANNER, new IntPtr(1), "Pergunte qualquer coisa sobre isso…");
        }

        // ---------- layout ----------

        int CardWidth
        {
            get
            {
                int pad = S(12), gap = S(8), w = pad * 2;
                foreach (var b in actions) w += b.Measure(S(12), S(6)).Width + gap;
                w += S(16); // breathing room between the two groups
                w += tools.Count * (S(34) + S(6));
                return Math.Max(S(440), w - gap);
            }
        }

        void DoLayout()
        {
            SuspendLayout();
            int pad = S(12), gap = S(8), W = CardWidth;
            int x = pad, y = pad, h = S(34);
            foreach (var b in actions)
            {
                int bw = b.Measure(S(12), S(6)).Width;
                b.SetBounds(x, y, bw, h);
                x += bw + gap;
            }
            int tx = W - pad;
            for (int i = tools.Count - 1; i >= 0; i--)
            {
                tx -= h;
                tools[i].SetBounds(tx, y, h, h);
                tx -= S(6);
            }
            y += h + S(10);

            int ih = S(42);
            inputShell.SetBounds(pad, y, W - pad * 2, ih);
            int sb = ih - S(10);
            sendBtn.SetBounds(inputShell.Width - sb - S(5), S(5), sb, sb);
            int th = input.PreferredHeight;
            input.SetBounds(S(14), (ih - th) / 2, inputShell.Width - sb - S(28), th);
            y += ih;

            if (smart.Count > 0)
            {
                y += S(8);
                int sx = pad;
                foreach (var c in smart)
                {
                    int cw = Math.Min(c.Measure(S(10), S(6)).Width, W - pad * 2);
                    bool fits = sx + cw <= W - pad; // never wrap: keep it one tidy line
                    c.Visible = fits;
                    if (!fits) continue;
                    c.SetBounds(sx, y, cw, S(28));
                    sx += cw + S(6);
                }
                y += S(28);
            }

            if (answerVisible)
            {
                y += S(12);
                answer.SetBounds(pad + S(2), y, W - pad * 2 - S(2), answerHeight);
                y += answerHeight + S(10);
                int rowH = S(28);
                var visibleBtn = keyBtn.Visible ? keyBtn : (folderBtn.Visible ? folderBtn : copyBtn);
                int bw = visibleBtn.Measure(S(10), S(6)).Width;
                visibleBtn.SetBounds(pad, y, bw, rowH);
                status.SetBounds(pad + bw + gap, y, W - pad * 2 - bw - gap, rowH);
                y += rowH;
            }
            y += pad;
            ClientSize = new Size(W, y);
            ResumeLayout();
            Reposition();
        }

        void Reposition()
        {
            Rectangle wa = Screen.FromPoint(new Point(selection.Left + selection.Width / 2, selection.Top + selection.Height / 2)).WorkingArea;
            int gap = S(10);
            int x = selection.Left + (selection.Width - Width) / 2;
            int y = selection.Top - Height - gap;                        // above the selection
            if (y < wa.Top) y = selection.Bottom + gap;                   // otherwise below
            if (y + Height > wa.Bottom) y = Math.Max(wa.Top, wa.Bottom - Height - gap); // otherwise inside
            x = Math.Max(wa.Left + gap, Math.Min(x, wa.Right - Width - gap));
            Location = new Point(x, y);
        }

        public void ShowCard()
        {
            Show();
            Native.ForceForeground(Handle);
            Activate();
            input.Focus();
            StartSmartActions();
        }

        // ---------- actions ----------

        string Lang { get { return cfg.TargetLanguage; } }

        string SystemPrompt
        {
            get
            {
                return "Você é um assistente embutido no Windows. O usuário selecionou uma área da tela; ela está na imagem anexada. " +
                       "Responda em " + Lang + ", de forma direta e concisa. Use Markdown leve (negrito e listas) só quando ajudar. " +
                       "Nunca diga que não consegue ver a tela: a imagem é a seleção.";
            }
        }

        void Translate()
        {
            Run("Traduzir",
                "Traduza todo o texto visível na imagem para " + Lang + ". Se o texto já estiver em " + Lang +
                ", traduza para inglês. Responda apenas com a tradução, preservando as quebras de linha. " +
                "Se não houver texto, descreva brevemente a imagem.", true, null);
        }

        void Explain()
        {
            Run("Explicar", "Explique de forma curta e clara o que é isso que eu selecionei.", true, null);
        }

        void CopyText()
        {
            Run("Copiar texto",
                "Transcreva exatamente o texto visível na imagem, preservando as quebras de linha e o idioma original. " +
                "Responda somente com o texto, sem comentários nem formatação Markdown. Se não houver texto, responda apenas: (sem texto)",
                false, delegate(string text)
                {
                    Program.Log("copy text result length=" + text.Length);
                    if (text.Length > 0 && text != "(sem texto)")
                    {
                        bool ok = SetClipboard(text);
                        if (!IsDisposed) status.Text = ok ? "Texto copiado ✓" : "Não consegui copiar";
                    }
                });
        }

        /// <summary>Copies the selected area as an image and saves it to Pictures\Magic Select.</summary>
        void TakePrint()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Magic Select");
            string file = Path.Combine(dir, "Print " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png");
            bool copied = false, saved = false;
            try { Clipboard.SetImage(shot); copied = true; }
            catch (Exception ex) { Program.Log("print clipboard error: " + ex); }
            try { Directory.CreateDirectory(dir); shot.Save(file, ImageFormat.Png); saved = true; }
            catch (Exception ex) { Program.Log("print save error: " + ex); }
            Program.Log("print copied=" + copied + " saved=" + saved + " " + file);

            if (!copied && !saved) { ShowError("Não consegui tirar o print."); return; }
            lastPrintFolder = dir;
            string msg = "📷  Print " + shot.Width + "×" + shot.Height + " " +
                         (copied ? "copiado — é só colar com Ctrl+V" : "") +
                         (copied && saved ? ", e " : "") +
                         (saved ? "salvo em Imagens\\Magic Select" : "") + ".";
            display.Add(new Item { Text = msg });
            keyBtn.Visible = false;
            folderBtn.Visible = saved;
            copyBtn.Visible = !saved;
            Render();
        }

        void Search()
        {
            if (!EnsureKey()) return;
            busy = true;
            SetStatus("Preparando pesquisa…");
            var turns = new List<Turn> { new Turn {
                Role = "user", ImageB64 = imageB64,
                Text = "Crie uma consulta curta de pesquisa no Google (no máximo 12 palavras) para encontrar informações sobre o conteúdo desta imagem. " +
                       "Se houver texto, baseie-se no texto principal. Responda somente com a consulta, sem aspas." } };
            string key = cfg.ApiKey, model = cfg.Model;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string q = Gemini.Ask(key, model, "Você gera consultas de pesquisa.", turns).Trim().Trim('"', '\'', '`').Replace("\n", " ");
                    app.Post(delegate
                    {
                        if (q.Length == 0) { if (!IsDisposed) { busy = false; ShowError("Não consegui criar uma pesquisa para isso."); } return; }
                        OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(q));
                        if (!IsDisposed) Close();
                    });
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    app.Post(delegate { if (!IsDisposed) { busy = false; ShowError(msg); } });
                }
            });
        }

        void OnInputKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AskTyped();
            }
        }

        void AskTyped()
        {
            string q = input.Text.Trim();
            if (q.Length == 0 || busy) return;
            input.Text = "";
            Run(q, q, true, null);
        }

        bool EnsureKey()
        {
            if (!string.IsNullOrEmpty(cfg.ApiKey)) return true;
            display.Clear();
            display.Add(new Item { Text = "Para usar a IA, cole sua chave grátis do Gemini nas Configurações. " +
                                          "Você consegue uma em aistudio.google.com/apikey" });
            keyBtn.Visible = true;
            copyBtn.Visible = false;
            Render();
            return false;
        }

        void SetStatus(string text)
        {
            status.Text = text;
            if (!answerVisible)
            {
                display.Add(new Item { IsUser = true, Text = text });
                Render();
            }
        }

        void ShowError(string message)
        {
            display.Add(new Item { IsError = true, Text = "⚠ " + message });
            Render();
        }

        /// <summary>Sends a prompt (with the selection image) and streams the answer into the card.</summary>
        void Run(string label, string prompt, bool keepHistory, Action<string> onDone)
        {
            if (busy || !EnsureKey()) return;
            busy = true;
            int id = ++requestId;
            keyBtn.Visible = false;
            folderBtn.Visible = false;
            copyBtn.Visible = true;
            status.Text = "Pensando…";

            var userTurn = new Turn { Role = "user", Text = prompt };
            List<Turn> send;
            if (keepHistory)
            {
                if (history.Count == 0) userTurn.ImageB64 = imageB64;
                history.Add(userTurn);
                send = new List<Turn>(history);
            }
            else
            {
                userTurn.ImageB64 = imageB64;
                send = new List<Turn> { userTurn };
            }

            display.Add(new Item { IsUser = true, Text = label });
            var ans = new Item();
            display.Add(ans);
            Render();

            string key = cfg.ApiKey, model = cfg.Model, system = SystemPrompt;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var full = new System.Text.StringBuilder();
                try
                {
                    Gemini.Stream(key, model, system, send, delegate(string chunk)
                    {
                        full.Append(chunk);
                        string soFar = full.ToString();
                        // Posted through the app (always alive), so a closed card never crashes the program.
                        app.Post(delegate
                        {
                            if (id != requestId || IsDisposed) return;
                            ans.Text = soFar;
                            Render();
                        });
                    });
                    string text = full.ToString().Trim();
                    app.Post(delegate
                    {
                        // onDone still runs if the card was closed meanwhile (e.g. "Copiar texto" then clicking away).
                        if (!IsDisposed)
                        {
                            busy = false;
                            lastAnswer = text;
                            if (keepHistory) history.Add(new Turn { Role = "model", Text = text });
                            status.Text = "Gemini";
                            if (text.Length == 0) { ans.Text = "(sem resposta)"; Render(); }
                        }
                        if (onDone != null) onDone(text);
                    });
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    Program.Log("request error: " + ex);
                    app.Post(delegate
                    {
                        if (IsDisposed) return;
                        busy = false;
                        if (keepHistory) history.Remove(userTurn);
                        ans.IsError = true;
                        ans.Text = "⚠ " + msg;
                        status.Text = "";
                        Render();
                    });
                }
            });
        }

        // ---------- rendering ----------

        void Render()
        {
            const int WM_SETREDRAW = 0x000B;
            if (!answerVisible)
            {
                // Give the box its real width first so word-wrap (and our height measurement) is right.
                answerVisible = true;
                answerHeight = S(40);
                DoLayout();
            }
            Native.SendMessage(answer.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            answer.ReadOnly = false;
            answer.Clear();
            for (int i = 0; i < display.Count; i++)
            {
                var it = display[i];
                if (i > 0) Append(it.IsUser ? "\n\n" : "\n", font, Theme.Text);
                if (it.IsUser) Append("› " + it.Text, italicFont, Theme.Muted);
                else if (it.IsError) Append(it.Text, font, Theme.Error);
                else if (it.Text.Length == 0) Append("…", font, Theme.Muted);
                else AppendMarkdown(it.Text);
            }
            answer.ReadOnly = true;
            Native.SendMessage(answer.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            answer.Invalidate();

            // Grow the card with the content, up to a max height, then scroll.
            answerVisible = true;
            answer.Visible = true;
            int contentH = answer.GetPositionFromCharIndex(Math.Max(0, answer.TextLength - 1)).Y + font.Height * 2;
            int firstY = answer.GetPositionFromCharIndex(0).Y;
            contentH -= Math.Min(0, firstY); // account for scrolling
            answerHeight = Math.Max(S(40), Math.Min(S(380), contentH));
            DoLayout();
            answer.SelectionStart = answer.TextLength;
            answer.ScrollToCaret();
        }

        void Append(string text, Font f, Color c)
        {
            answer.SelectionStart = answer.TextLength;
            answer.SelectionLength = 0;
            answer.SelectionFont = f;
            answer.SelectionColor = c;
            answer.SelectedText = text;
        }

        void AppendMarkdown(string md)
        {
            string[] lines = md.Replace("\r", "").Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                if (li > 0) Append("\n", font, Theme.Text);
                string trimmed = line.TrimStart();
                int indent = line.Length - trimmed.Length;
                bool heading = false;
                if (trimmed.StartsWith("#"))
                {
                    trimmed = trimmed.TrimStart('#').TrimStart();
                    heading = true;
                }
                else if (trimmed.StartsWith("* ") || trimmed.StartsWith("- "))
                {
                    trimmed = new string(' ', indent) + "•  " + trimmed.Substring(2);
                }
                else trimmed = line;

                string[] parts = trimmed.Replace("__", "**").Split(new[] { "**" }, StringSplitOptions.None);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length == 0) continue;
                    bool bold = heading || (i % 2 == 1);
                    Append(parts[i].Replace("`", ""), bold ? boldFont : font, Theme.Text);
                }
            }
        }

        // ---------- misc ----------

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); return; }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            requestId++;
            if (synth != null) { try { synth.SpeakAsyncCancelAll(); synth.Dispose(); } catch { } synth = null; }
            base.OnFormClosed(e);
            app.CardClosed(this);
        }

        static bool SetClipboard(string text)
        {
            try
            {
                // Other apps (clipboard history, browsers…) sometimes hold the clipboard: retry for ~1s.
                Clipboard.SetDataObject(text, true, 10, 100);
                return true;
            }
            catch (Exception ex)
            {
                Program.Log("clipboard error: " + ex);
                return false;
            }
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        static string EncodeImage(Bitmap img)
        {
            // Keep it small for speed, but upscale tiny selections so text stays readable.
            const int maxSide = 1600;
            int w = img.Width, h = img.Height;
            float k = 1f;
            int longest = Math.Max(w, h);
            if (longest > maxSide) k = (float)maxSide / longest;
            else if (longest < 400) k = Math.Min(3f, 400f / longest);

            Bitmap use = img;
            if (Math.Abs(k - 1f) > 0.01f)
            {
                use = new Bitmap(Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)));
                using (var g = Graphics.FromImage(use))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, 0, 0, use.Width, use.Height);
                }
            }
            using (var ms = new MemoryStream())
            {
                use.Save(ms, ImageFormat.Png);
                if (!ReferenceEquals(use, img)) use.Dispose();
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                shot.Dispose();
                tips.Dispose();
                font.Dispose(); smallFont.Dispose(); boldFont.Dispose(); italicFont.Dispose(); iconFont.Dispose();
            }
        }
    }
}
