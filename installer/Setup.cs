using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace MagicSelect
{
    static class SetupProgram
    {
        [STAThread]
        static void Main()
        {
            Native.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    /// <summary>One-window installer: options → install → open.</summary>
    class SetupForm : DarkWindow
    {
        readonly ToggleSwitch desktopSw, startupSw;
        readonly PillButton installBtn;
        readonly Label status;
        readonly Panel options, done;
        readonly bool update;

        const int W = 520, H = 470;

        public SetupForm()
        {
            update = Installation.IsInstalled;
            Text = (update ? "Atualizar " : "Instalar ") + Brand.Name;
            ShowInTaskbar = true;
            ClientSize = new Size(S(W), S(H));
            int pad = S(40), cw = S(W) - pad * 2, y = S(36);

            var logo = new PictureBox { Image = Brand.GetLogo(S(84)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            logo.SetBounds((S(W) - S(84)) / 2, y, S(84), S(84));
            Controls.Add(logo);
            y += S(102);
            CenterText(AddLabel(this, Brand.Name, titleFont, Theme.Text, pad, y, cw)); y += S(36);
            CenterText(AddLabel(this, Brand.Tagline, smallFont, Theme.Muted, pad, y, cw)); y += S(22);
            CenterText(AddLabel(this, "Versão " + Brand.Version, smallFont, Theme.Muted, pad, y, cw)); y += S(36);

            // options
            options = new CardPanel();
            int cy = S(6);
            desktopSw = Row(options, ref cy, cw - S(32), "Atalho na Área de Trabalho", true);
            startupSw = Row(options, ref cy, cw - S(32), "Iniciar junto com o Windows", true);
            cy += S(6);
            options.SetBounds(pad, y, cw, cy);
            Controls.Add(options);

            // finished panel (same place)
            done = new CardPanel { Visible = false };
            done.SetBounds(pad, y, cw, cy);
            var ok = AddLabel(done, "✓  " + Brand.Name + (update ? " foi atualizado!" : " foi instalado!"), boldFont, Theme.Success, S(16), S(18), cw - S(32));
            AddLabel(done, "Ele fica perto do relógio. Na primeira vez, um guia rápido ensina a usar.", smallFont, Theme.Muted, S(16), ok.Bottom + S(4), cw - S(32));
            Controls.Add(done);
            y += cy + S(20);

            installBtn = MakeButton(update ? "Atualizar" : "Instalar", "", true);
            installBtn.SetBounds(pad, y, cw, S(46));
            installBtn.Click += delegate { OnInstallClick(); };
            Controls.Add(installBtn);
            y += S(56);

            status = new Label { AutoSize = false, Font = smallFont, ForeColor = Theme.Muted, BackColor = Color.Transparent, TextAlign = ContentAlignment.TopCenter };
            status.Text = "Instala só para o seu usuário. Não precisa de administrador.";
            status.SetBounds(pad, y, cw, S(40));
            Controls.Add(status);

            CenterOnScreen();
        }

        void CenterText(Label l) { l.TextAlign = ContentAlignment.TopCenter; }

        ToggleSwitch Row(Panel p, ref int y, int inner, string text, bool value)
        {
            y += S(10);
            var l = AddLabel(p, text, font, Theme.Text, S(16), y + S(3), inner - S(60));
            var sw = new ToggleSwitch { Checked = value };
            sw.SetBounds(S(16) + inner - S(46), y, S(46), S(24));
            p.Controls.Add(sw);
            y += S(34);
            return sw;
        }

        bool installed;

        void OnInstallClick()
        {
            if (installed)
            {
                try { Installation.Launch(); } catch (Exception ex) { status.ForeColor = Theme.Error; status.Text = ex.Message; return; }
                Close();
                return;
            }

            installBtn.Enabled = false;
            installBtn.Text = update ? "Atualizando…" : "Instalando…";
            installBtn.Invalidate();
            bool desktop = desktopSw.Checked, startup = startupSw.Checked;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try
                {
                    using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.exe"))
                    {
                        if (payload == null) throw new InvalidOperationException("Instalador corrompido (arquivo do programa não encontrado).");
                        Installation.Install(payload, desktop, startup);
                    }
                }
                catch (Exception ex) { error = ex.Message; }

                BeginInvoke((MethodInvoker)delegate
                {
                    installBtn.Enabled = true;
                    if (error != null)
                    {
                        installBtn.Text = "Tentar de novo";
                        status.ForeColor = Theme.Error;
                        status.Text = "Não deu certo: " + error;
                    }
                    else
                    {
                        installed = true;
                        options.Visible = false;
                        done.Visible = true;
                        installBtn.Text = "Abrir " + Brand.Name;
                        installBtn.Icon = "";
                        status.ForeColor = Theme.Muted;
                        status.Text = "Para desinstalar: Configurações do Windows › Aplicativos › " + Brand.Name + ".";
                    }
                    installBtn.Invalidate();
                });
            });
        }
    }
}
