using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MagicSelect
{
    class SettingsForm : DarkWindow
    {
        readonly Config cfg;
        readonly VoiceCommands voice;
        readonly Panel content;
        readonly TextField keyField, modelField, langField, excludedField;
        readonly ToggleSwitch enabledSw, smartSw, gameSw, startupSw, historySw;
        readonly Label keyStatus, micLabel;
        int pendingKey, pendingMods;
        string pendingMouse;

        const int W = 600;

        public SettingsForm(Config cfg, VoiceCommands voice)
        {
            this.cfg = cfg;
            this.voice = voice;
            pendingKey = cfg.VoiceKey;
            pendingMods = cfg.VoiceModifiers;
            pendingMouse = cfg.VoiceMouse;
            Text = "Configurações — " + Brand.Name;
            ShowInTaskbar = true;
            KeyPreview = true;

            content = new Panel { AutoScroll = true, BackColor = Theme.Bg };
            Controls.Add(content);

            int pad = S(24), cw = S(W) - pad * 2, y = S(20);

            // ---- header
            var logo = new PictureBox { Image = Brand.GetLogo(S(44)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            logo.SetBounds(pad, y, S(44), S(44));
            content.Controls.Add(logo);
            AddLabel(content, Brand.Name, titleFont, Theme.Text, pad + S(56), y - S(2), cw - S(56));
            AddLabel(content, "Versão " + Brand.Version + "  ·  " + Brand.Tagline, smallFont, Theme.Muted, pad + S(56), y + S(28), cw - S(56));
            y += S(64);

            // ---- AI
            y = SectionTitle("Inteligência artificial", pad, y, cw);
            var ai = new CardPanel();
            int cy = S(16), inner = cw - S(32);
            AddLabel(ai, "Chave do Gemini", boldFont, Theme.Text, S(16), cy, inner); cy += S(24);
            AddLabel(ai, "É grátis. Cada pessoa usa a sua própria chave.", smallFont, Theme.Muted, S(16), cy, inner); cy += S(26);
            keyField = new TextField(font, true);
            keyField.Value = cfg.ApiKey;
            keyField.SetPlaceholder("Cole aqui a sua chave (começa com AIza…)");
            keyField.ReservedRight = S(40);
            keyField.SetBounds(S(16), cy, inner, S(40));
            var eye = new PillButton { Icon = "", IconFont = iconFont, Font = font };
            eye.SetBounds(keyField.Width - S(36), S(4), S(32), S(32));
            eye.Click += delegate { keyField.Box.UseSystemPasswordChar = !keyField.Box.UseSystemPasswordChar; };
            keyField.Controls.Add(eye);
            ai.Controls.Add(keyField);
            cy += S(50);

            var getKey = MakeButton("Pegar chave grátis", "", false);
            getKey.Font = smallFont;
            getKey.SetBounds(S(16), cy, getKey.Measure(S(14), S(6)).Width, S(32));
            getKey.Click += delegate { Open("https://aistudio.google.com/apikey"); };
            ai.Controls.Add(getKey);
            var test = MakeButton("Testar chave", "", false);
            test.Font = smallFont;
            test.SetBounds(getKey.Right + S(8), cy, test.Measure(S(14), S(6)).Width, S(32));
            test.Click += delegate { TestKey(); };
            ai.Controls.Add(test);
            keyStatus = new Label { AutoSize = false, Font = smallFont, ForeColor = Theme.Muted, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
            keyStatus.SetBounds(test.Right + S(12), cy, inner - test.Right, S(32));
            ai.Controls.Add(keyStatus);
            cy += S(48);

            int half = (inner - S(12)) / 2;
            AddLabel(ai, "Modelo", smallFont, Theme.Muted, S(16), cy, half);
            AddLabel(ai, "Idioma das respostas", smallFont, Theme.Muted, S(16) + half + S(12), cy, half);
            cy += S(22);
            modelField = new TextField(font, false) { Value = cfg.Model };
            modelField.SetBounds(S(16), cy, half, S(40));
            ai.Controls.Add(modelField);
            langField = new TextField(font, false) { Value = cfg.TargetLanguage };
            langField.SetBounds(S(16) + half + S(12), cy, half, S(40));
            ai.Controls.Add(langField);
            cy += S(56);
            ai.SetBounds(pad, y, cw, cy);
            content.Controls.Add(ai);
            y += cy + S(20);

            // ---- selection
            y = SectionTitle("Seleção com o botão direito", pad, y, cw);
            var sel = new CardPanel();
            cy = S(6);
            enabledSw = ToggleRow(sel, ref cy, inner, "Ativado", "Segure o botão direito e arraste sobre qualquer coisa.", cfg.Enabled);
            smartSw = ToggleRow(sel, ref cy, inner, "Ações inteligentes", "Mostra atalhos para Maps, links, WhatsApp, contas e moedas. Envia a seleção ao Gemini ao abrir.", cfg.SmartActions);
            gameSw = ToggleRow(sel, ref cy, inner, "Desativar quando estiver jogando", "Fica desligado enquanto um programa estiver em tela cheia.", cfg.DisableInFullscreen);
            cy += S(6);
            AddLabel(sel, "Ignorar nestes programas", smallFont, Theme.Muted, S(16), cy, inner); cy += S(22);
            excludedField = new TextField(font, false) { Value = cfg.ExcludedApps };
            excludedField.SetPlaceholder("ex.: valorant.exe, blender.exe");
            excludedField.SetBounds(S(16), cy, inner, S(40));
            sel.Controls.Add(excludedField);
            cy += S(56);
            sel.SetBounds(pad, y, cw, cy);
            content.Controls.Add(sel);
            y += cy + S(20);

            // ---- voice
            y = SectionTitle("Comando de voz", pad, y, cw);
            var vc = new CardPanel();
            cy = S(16);
            AddLabel(vc, "Botão do microfone", boldFont, Theme.Text, S(16), cy, inner); cy += S(24);
            AddLabel(vc, "Segure, fale e solte. Ex.: \"toca Evidências no YouTube\", \"abre a calculadora\".", smallFont, Theme.Muted, S(16), cy, inner); cy += S(30);
            micLabel = new Label { AutoSize = false, Font = boldFont, ForeColor = Theme.Accent, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
            var micBtn = MakeButton("Trocar botão", "", false);
            micBtn.Font = smallFont;
            int mbw = micBtn.Measure(S(14), S(6)).Width;
            micBtn.SetBounds(S(16) + inner - mbw, cy, mbw, S(34));
            micBtn.Click += delegate { CaptureButton(); };
            micLabel.SetBounds(S(16), cy, inner - mbw - S(12), S(34));
            vc.Controls.Add(micLabel);
            vc.Controls.Add(micBtn);
            UpdateMicLabel();
            cy += S(50);
            vc.SetBounds(pad, y, cw, cy);
            content.Controls.Add(vc);
            y += cy + S(20);

            // ---- general
            y = SectionTitle("Geral", pad, y, cw);
            var gen = new CardPanel();
            cy = S(6);
            startupSw = ToggleRow(gen, ref cy, inner, "Iniciar junto com o Windows", "Fica pronto perto do relógio assim que você liga o PC.", Config.StartsWithWindows);
            historySw = ToggleRow(gen, ref cy, inner, "Histórico de cópias", "Guarda o que você copia (Ctrl+C) para usar de novo. Senhas de gerenciadores de senha não são guardadas.", cfg.ClipboardHistory);
            cy += S(6);
            gen.SetBounds(pad, y, cw, cy);
            content.Controls.Add(gen);
            y += cy + S(24);

            // ---- footer (fixed at the bottom)
            var footer = new Panel { BackColor = Theme.Bg, Dock = DockStyle.Bottom, Height = S(72) };
            var save = MakeButton("Salvar", "", true);
            var cancel = MakeButton("Cancelar", null, false);
            int sw = save.Measure(S(18), S(8)).Width, cwid = cancel.Measure(S(18), S(8)).Width;
            save.SetBounds(S(W) - pad - sw, S(16), sw, S(40));
            cancel.SetBounds(save.Left - S(10) - cwid, S(16), cwid, S(40));
            save.Click += delegate { Save(); Close(); };
            cancel.Click += delegate { Close(); };
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            var help = new LinkLabel { Text = "Como usar", Font = smallFont, LinkColor = Theme.Muted, ActiveLinkColor = Theme.Accent, BackColor = Color.Transparent, AutoSize = true };
            help.Location = new Point(pad, S(27));
            help.LinkClicked += delegate { new WelcomeForm(cfg, null, false).Show(); }; // mic button is changed right here in Settings
            footer.Controls.Add(help);
            Controls.Add(footer);

            // Fit the screen; scroll if it doesn't.
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int contentH = y;
            int clientH = Math.Min(contentH + footer.Height, (int)(wa.Height * 0.9));
            ClientSize = new Size(S(W) + (contentH + footer.Height > clientH ? SystemInformation.VerticalScrollBarWidth : 0), clientH);
            content.SetBounds(0, 0, ClientSize.Width, clientH - footer.Height);
            CenterOnScreen();
        }

        int SectionTitle(string text, int x, int y, int w)
        {
            AddLabel(content, text.ToUpperInvariant(), smallFont, Theme.Accent, x + S(4), y, w);
            return y + S(26);
        }

        ToggleSwitch ToggleRow(Panel parent, ref int y, int inner, string title, string subtitle, bool value)
        {
            int textW = inner - S(64);
            y += S(12);
            var t = AddLabel(parent, title, boldFont, Theme.Text, S(16), y, textW);
            var sub = AddLabel(parent, subtitle, smallFont, Theme.Muted, S(16), t.Bottom, textW);
            var sw = new ToggleSwitch { Checked = value };
            int rowH = sub.Bottom - y;
            sw.SetBounds(S(16) + inner - S(46), y + (rowH - S(24)) / 2, S(46), S(24));
            parent.Controls.Add(sw);
            y = sub.Bottom + S(10);
            return sw;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = null; // don't open with the key field selected
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        void TestKey()
        {
            keyStatus.ForeColor = Theme.Muted;
            keyStatus.Text = "Testando…";
            string key = keyField.Value, model = string.IsNullOrWhiteSpace(modelField.Value) ? Config.DefaultModel : modelField.Value.Trim();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = Gemini.TestKey(key, model);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        keyStatus.ForeColor = err == null ? Theme.Success : Theme.Error;
                        keyStatus.Text = err == null ? "✓ Funcionando!" : err;
                    });
                }
                catch { } // window closed meanwhile
            });
        }

        void UpdateMicLabel()
        {
            micLabel.Text = VoiceTrigger.Describe(pendingKey, pendingMods, pendingMouse);
        }

        void CaptureButton()
        {
            voice.Suspend(); // so the current button doesn't fire while choosing a new one
            using (var dlg = new ButtonCaptureForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    pendingKey = dlg.Key;
                    pendingMods = dlg.Modifiers;
                    pendingMouse = dlg.Mouse;
                    UpdateMicLabel();
                }
            }
            // The new button only takes effect after "Salvar"; until then keep the current one.
            voice.ApplyTrigger();
        }

        void Save()
        {
            cfg.ApiKey = keyField.Value.Trim();
            cfg.Model = string.IsNullOrWhiteSpace(modelField.Value) ? Config.DefaultModel : modelField.Value.Trim();
            cfg.TargetLanguage = string.IsNullOrWhiteSpace(langField.Value) ? "português do Brasil" : langField.Value.Trim();
            cfg.ExcludedApps = excludedField.Value.Trim();
            cfg.Enabled = enabledSw.Checked;
            cfg.SmartActions = smartSw.Checked;
            cfg.ClipboardHistory = historySw.Checked;
            cfg.DisableInFullscreen = gameSw.Checked;
            cfg.VoiceKey = pendingKey;
            cfg.VoiceModifiers = pendingMods;
            cfg.VoiceMouse = pendingMouse ?? "";
            cfg.Save();
            try { Config.StartsWithWindows = startupSw.Checked; } catch { }
        }

        static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
