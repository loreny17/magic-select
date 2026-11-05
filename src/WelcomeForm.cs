using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace MagicSelect
{
    /// <summary>First-run tour: what it does → connect the free Gemini key → try it.</summary>
    class WelcomeForm : DarkWindow
    {
        readonly Config cfg;
        readonly VoiceCommands voice;
        readonly bool firstRun;
        readonly Panel[] pages = new Panel[4];
        readonly Label micLabel;
        readonly PillButton next, back;
        readonly TextField keyField;
        readonly Label keyStatus;
        int page;
        bool keyOk;

        const int W = 580, H = 560;

        public WelcomeForm(Config cfg, VoiceCommands voice, bool firstRun)
        {
            this.cfg = cfg;
            this.voice = voice;
            this.firstRun = firstRun;
            Text = "Bem-vindo ao " + Brand.Name;
            ShowInTaskbar = true;
            ClientSize = new Size(S(W), S(H));
            int pad = S(36), cw = S(W) - pad * 2;

            for (int i = 0; i < pages.Length; i++)
            {
                pages[i] = new Panel { BackColor = Theme.Bg, Visible = i == 0 };
                pages[i].SetBounds(0, 0, S(W), S(H) - S(84));
                Controls.Add(pages[i]);
            }

            // ---------- page 1: what it does
            var p = pages[0];
            int y = S(40);
            var logo = new PictureBox { Image = Brand.GetLogo(S(80)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            logo.SetBounds((S(W) - S(80)) / 2, y, S(80), S(80));
            p.Controls.Add(logo);
            y += S(100);
            Center(AddLabel(p, "Bem-vindo ao " + Brand.Name, titleFont, Theme.Text, pad, y, cw)); y += S(40);
            Center(AddLabel(p, Brand.Tagline, font, Theme.Muted, pad, y, cw)); y += S(52);
            y = Feature(p, "", "Segure o botão direito e arraste", "sobre qualquer coisa na tela: texto, imagem, vídeo, PDF…", pad, y, cw);
            y = Feature(p, "", "Traduza, pesquise ou pergunte", "e ainda copie o texto, tire print ou ouça em voz alta.", pad, y, cw);
            y = Feature(p, "", "Fale com o seu PC", "Segure o botão do microfone e diga \"toca Evidências no YouTube\".", pad, y, cw);

            // ---------- page 2: the key
            p = pages[1];
            y = S(40);
            AddLabel(p, "Conecte a IA", titleFont, Theme.Text, pad, y, cw); y += S(40);
            AddLabel(p, "O " + Brand.Name + " usa o Gemini, do Google. É grátis, só precisa de uma chave da sua conta Google (leva 1 minuto).", font, Theme.Muted, pad, y, cw);
            y += S(64);
            y = Step(p, "1", "Clique em \"Pegar chave grátis\" e entre com sua conta Google.", pad, y, cw);
            y = Step(p, "2", "Clique em \"Create API key\" (Criar chave de API) e copie a chave.", pad, y, cw);
            y = Step(p, "3", "Cole a chave aqui embaixo e clique em Testar.", pad, y, cw);
            y += S(8);
            var getKey = MakeButton("Pegar chave grátis", "", true);
            getKey.SetBounds(pad, y, getKey.Measure(S(16), S(8)).Width, S(40));
            getKey.Click += delegate { Open("https://aistudio.google.com/apikey"); };
            p.Controls.Add(getKey);
            y += S(56);
            keyField = new TextField(font, true) { Value = cfg.ApiKey };
            keyField.SetPlaceholder("Cole aqui a sua chave (começa com AIza…)");
            int testW = S(110);
            keyField.SetBounds(pad, y, cw - testW - S(10), S(42));
            p.Controls.Add(keyField);
            var test = MakeButton("Testar", "", false);
            test.SetBounds(pad + cw - testW, y + S(1), testW, S(40));
            test.Click += delegate { TestKey(); };
            p.Controls.Add(test);
            y += S(50);
            keyStatus = new Label { AutoSize = false, Font = smallFont, ForeColor = Theme.Muted, BackColor = Color.Transparent };
            keyStatus.SetBounds(pad, y, cw, S(40));
            p.Controls.Add(keyStatus);

            // ---------- page 3: microphone button
            p = pages[2];
            y = S(40);
            AddLabel(p, "Botão do microfone", titleFont, Theme.Text, pad, y, cw); y += S(40);
            AddLabel(p, "Escolha a tecla ou o botão do mouse que você vai segurar para falar comandos, " +
                        "como \"toca Evidências no YouTube\" ou \"abre a calculadora\".", font, Theme.Muted, pad, y, cw);
            y += S(76);
            var micCard = new CardPanel();
            micCard.SetBounds(pad, y, cw, S(110));
            var micIcon = new Label { Text = "", Font = new Font("Segoe MDL2 Assets", S(26), GraphicsUnit.Pixel), ForeColor = Theme.Accent,
                                      BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter };
            micIcon.SetBounds(S(16), S(25), S(60), S(60));
            micCard.Controls.Add(micIcon);
            AddLabel(micCard, "Seu botão", smallFont, Theme.Muted, S(88), S(26), cw - S(104));
            micLabel = new Label { Font = boldFont, ForeColor = Theme.Text, BackColor = Color.Transparent, AutoSize = false };
            micLabel.SetBounds(S(88), S(48), cw - S(104), S(30));
            micCard.Controls.Add(micLabel);
            p.Controls.Add(micCard);
            y += S(126);
            var choose = MakeButton("Escolher botão", "", true);
            choose.SetBounds(pad, y, choose.Measure(S(16), S(8)).Width, S(42));
            choose.Click += delegate { ChooseMicButton(); };
            p.Controls.Add(choose);
            if (voice == null)
            {
                // Opened from Settings: the button is changed there, avoid two places editing it at once.
                choose.Visible = false;
                AddLabel(p, "Para trocar, use \"Trocar botão\" nas Configurações.", smallFont, Theme.Muted, pad, y, cw);
            }
            else
            {
                y += S(58);
                AddLabel(p, "Dica: botões laterais do mouse são ótimos para isso. Dá para trocar depois nas Configurações.", smallFont, Theme.Muted, pad, y, cw);
            }
            UpdateMicLabel();

            // ---------- page 4: try it
            p = pages[3];
            y = S(40);
            AddLabel(p, "Tudo pronto!", titleFont, Theme.Text, pad, y, cw); y += S(44);
            AddLabel(p, "Experimente agora: segure o botão direito do mouse e arraste sobre o texto abaixo. Depois clique em Traduzir.", font, Theme.Muted, pad, y, cw);
            y += S(64);
            var sample = new CardPanel();
            sample.SetBounds(pad, y, cw, S(120));
            var sampleText = new Label
            {
                Text = "\"The best way to predict the future is to invent it.\"\n— Alan Kay",
                Font = new Font("Georgia", S(19), FontStyle.Italic, GraphicsUnit.Pixel),
                ForeColor = Theme.Text, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter
            };
            sampleText.SetBounds(S(16), S(10), cw - S(32), S(100));
            sample.Controls.Add(sampleText);
            p.Controls.Add(sample);
            y += S(140);
            AddLabel(p, "Dica: o ícone " + Brand.Name + " fica perto do relógio. Clique nele para falar um comando, ou com o botão direito para abrir as configurações.", smallFont, Theme.Muted, pad, y, cw);

            // ---------- footer
            back = MakeButton("Voltar", null, false);
            next = MakeButton("Começar", "", true);
            back.SetBounds(pad, S(H) - S(64), S(110), S(42));
            next.SetBounds(S(W) - pad - S(150), S(H) - S(64), S(150), S(42));
            back.Click += delegate { Go(page - 1); };
            next.Click += delegate { if (page == pages.Length - 1) Finish(); else Go(page + 1); };
            Controls.Add(back);
            Controls.Add(next);
            Go(0);
            CenterOnScreen();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = next;
        }

        void Center(Label l) { l.TextAlign = ContentAlignment.TopCenter; }

        int Feature(Panel p, string icon, string title, string text, int x, int y, int w)
        {
            var bubble = new Label { Text = icon, Font = new Font("Segoe MDL2 Assets", S(18), GraphicsUnit.Pixel), ForeColor = Theme.Accent,
                                     BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleCenter };
            bubble.SetBounds(x, y, S(44), S(44));
            p.Controls.Add(bubble);
            bubble.HandleCreated += delegate
            {
                using (var path = Theme.RoundRect(new RectangleF(0, 0, bubble.Width, bubble.Height), S(12)))
                    bubble.Region = new Region(path);
            };
            AddLabel(p, title, boldFont, Theme.Text, x + S(60), y + S(1), w - S(60));
            AddLabel(p, text, smallFont, Theme.Muted, x + S(60), y + S(22), w - S(60));
            return y + S(62);
        }

        int Step(Panel p, string n, string text, int x, int y, int w)
        {
            var dot = new Label { Text = n, Font = boldFont, ForeColor = Theme.Bg, BackColor = Theme.Accent, TextAlign = ContentAlignment.MiddleCenter };
            dot.SetBounds(x, y, S(26), S(26));
            dot.HandleCreated += delegate
            {
                using (var path = new GraphicsPath()) { path.AddEllipse(0, 0, dot.Width, dot.Height); dot.Region = new Region(path); }
            };
            p.Controls.Add(dot);
            var l = AddLabel(p, text, font, Theme.Text, x + S(38), y + S(2), w - S(38));
            return Math.Max(y + S(34), l.Bottom + S(8));
        }

        void Go(int i)
        {
            if (i < 0 || i >= pages.Length) return;
            page = i;
            for (int k = 0; k < pages.Length; k++) pages[k].Visible = k == i;
            back.Visible = i > 0;
            next.Text = i == 0 ? "Começar" : (i == pages.Length - 1 ? "Concluir" :
                        (i == 1 && !keyOk && keyField.Value.Trim().Length == 0 ? "Pular" : "Próximo"));
            next.Icon = i == pages.Length - 1 ? "" : "";
            next.Invalidate();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // page dots
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int d = S(8), gap = S(10), total = pages.Length * d + (pages.Length - 1) * gap;
            int x = (ClientSize.Width - total) / 2, y = S(H) - S(43) - d / 2;
            for (int i = 0; i < pages.Length; i++)
                using (var b = new SolidBrush(i == page ? Theme.Accent : Theme.Border))
                    g.FillEllipse(b, x + i * (d + gap), y, d, d);
        }

        void UpdateMicLabel()
        {
            micLabel.Text = VoiceTrigger.Describe(cfg);
        }

        void ChooseMicButton()
        {
            if (voice != null) voice.Suspend(); // so the current button doesn't fire while choosing
            using (var dlg = new ButtonCaptureForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    cfg.VoiceKey = dlg.Key;
                    cfg.VoiceModifiers = dlg.Modifiers;
                    cfg.VoiceMouse = dlg.Mouse;
                    cfg.Save();
                    UpdateMicLabel();
                }
            }
            if (voice != null && !voice.ApplyTrigger())
            {
                micLabel.Text = VoiceTrigger.Describe(cfg) + "  (já usado por outro programa — escolha outro)";
                micLabel.ForeColor = Theme.Error;
            }
            else micLabel.ForeColor = Theme.Text;
        }

        void TestKey()
        {
            keyStatus.ForeColor = Theme.Muted;
            keyStatus.Text = "Testando…";
            string key = keyField.Value;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = Gemini.TestKey(key, cfg.Model);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        keyOk = err == null;
                        keyStatus.ForeColor = keyOk ? Theme.Success : Theme.Error;
                        keyStatus.Text = keyOk ? "✓ Chave funcionando! Clique em Próximo." : err;
                        if (keyOk) SaveKey();
                        Go(page);
                    });
                }
                catch { }
            });
        }

        void SaveKey()
        {
            string key = keyField.Value.Trim();
            if (key.Length > 0) { cfg.ApiKey = key; cfg.Save(); }
        }

        void Finish()
        {
            SaveKey();
            cfg.WelcomeShown = true;
            cfg.Save();
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (firstRun && !cfg.WelcomeShown) { cfg.WelcomeShown = true; SaveKey(); cfg.Save(); }
            base.OnFormClosed(e);
        }

        static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
