using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace MagicSelect
{
    static class Program
    {
        static readonly bool debug = Environment.GetCommandLineArgs().Length > 1 &&
                                     Environment.GetCommandLineArgs()[1].StartsWith("--");

        public static void Log(string msg)
        {
            if (!debug) return;
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MagicSelect");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "debug.log"), DateTime.Now.ToString("HH:mm:ss.fff ") + msg + "\r\n");
            }
            catch { }
        }

        [STAThread]
        static void Main()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && args[1] == "--uninstall")
            {
                Native.EnableDpiAwareness();
                Application.EnableVisualStyles();
                if (MessageBox.Show("Desinstalar o " + Brand.Name + "?\n\nSuas configurações também serão apagadas.", Brand.Name,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Installation.Uninstall();
                    MessageBox.Show(Brand.Name + " foi desinstalado.", Brand.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, "MagicSelect_SingleInstance_v1", out created))
            {
                if (!created)
                {
                    MessageBox.Show("O Magic Select já está rodando — veja o ícone perto do relógio.", "Magic Select");
                    return;
                }
                Native.EnableDpiAwareness();
                // Never die silently: log unexpected errors and keep running.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log("ui error: " + e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log("fatal: " + e.ExceptionObject); };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; // TLS 1.2
                try
                {
                    Application.Run(new App());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Magic Select", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                GC.KeepAlive(mutex);
            }
        }
    }

    class App : ApplicationContext
    {
        readonly Config cfg;
        readonly Control ui;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem enabledItem;
        readonly MouseHook hook;
        readonly SelectionBox box;
        readonly VoiceCommands voice;
        ActionCard card;
        SettingsForm settings;
        Bitmap snapshot;
        Rectangle snapshotBounds;

        public App()
        {
            cfg = Config.Load();
            ui = new Control();
            ui.CreateControl();
            var forceHandle = ui.Handle;

            box = new SelectionBox();

            var menu = new ContextMenuStrip();
            menu.Renderer = new DarkMenuRenderer();
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            var header = new ToolStripMenuItem(Brand.Name + "  " + Brand.Version) { Enabled = false };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());
            enabledItem = new ToolStripMenuItem("Ativado", null, delegate { ToggleEnabled(); });
            enabledItem.Checked = cfg.Enabled;
            menu.Items.Add(enabledItem);
            menu.Items.Add("🎤  Falar um comando", null, delegate { voice.Toggle(); });
            menu.Items.Add("⚙  Configurações", null, delegate { ShowSettings(); });
            menu.Items.Add("📋  Histórico de cópias", null, delegate
            {
                Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                ShowHistory(new Point(wa.Right, wa.Bottom)); // bottom-right, next to the tray
            });
            menu.Items.Add("❔  Como usar", null, delegate { ShowWelcome(false); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair", null, delegate { ExitThread(); });

            tray = new NotifyIcon();
            tray.Icon = Brand.GetIcon(SystemInformation.SmallIconSize.Width);
            tray.Text = Brand.Name + " — segure o botão direito e arraste";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            // Left click on the tray icon = microphone.
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) voice.Toggle(); };
            voice = new VoiceCommands(this, cfg);
            AppIndex.RefreshAsync();
            clips = new ClipboardHistory();
            if (cfg.ClipboardHistory) clips.Start();

            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 2 && args[1] == "--voice-test")
            {
                byte[] wav = System.IO.File.ReadAllBytes(args[2]);
                ui.BeginInvoke((MethodInvoker)delegate { voice.RunAudio(wav, true); });
            }

            hook = new MouseHook(ui);
            hook.IsEnabled = delegate { return cfg.Enabled; };
            hook.IsExcludedProcess = cfg.IsExcluded;
            hook.SkipFullscreen = delegate { return cfg.DisableInFullscreen; };
            hook.DragStarted = OnDragStarted;
            hook.DragMoved = OnDragMoved;
            hook.DragEnded = OnDragEnded;
            hook.LeftDown = OnLeftDown;
            hook.ExtraButton = delegate(string button, bool down)
            {
                if (cfg.VoiceMouse != button) return false;
                if (cfg.DisableInFullscreen && Native.IsFullscreenAppActive()) return false; // gaming: leave it to the game
                return voice.OnMouseButton(button, down);
            };

            if (!cfg.WelcomeShown)
            {
                ui.BeginInvoke((MethodInvoker)delegate { ShowWelcome(true); });
            }
            else if (string.IsNullOrEmpty(cfg.ApiKey))
            {
                tray.ShowBalloonTip(6000, Brand.Name + " está rodando",
                    "Falta conectar a IA: abra as Configurações e cole sua chave grátis do Gemini.", ToolTipIcon.Info);
            }
            else
            {
                tray.ShowBalloonTip(3000, Brand.Name + " está rodando", "Segure o botão direito do mouse e arraste sobre qualquer coisa.", ToolTipIcon.None);
            }
        }

        static Rectangle Normalize(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        void OnDragStarted(Point start)
        {
            Program.Log("drag start " + start);
            if (card != null) card.Close();
            // Freeze the screen as it is now; the selection is cropped from this later.
            if (snapshot != null) snapshot.Dispose();
            snapshotBounds = SystemInformation.VirtualScreen;
            snapshot = new Bitmap(snapshotBounds.Width, snapshotBounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(snapshot))
                g.CopyFromScreen(snapshotBounds.Left, snapshotBounds.Top, 0, 0, snapshotBounds.Size, CopyPixelOperation.SourceCopy);
            box.ShowRect(Normalize(start, start));
        }

        void OnDragMoved(Point start, Point now)
        {
            box.ShowRect(Normalize(start, now));
        }

        void OnDragEnded(Point start, Point end)
        {
            Rectangle r = Normalize(start, end);
            Program.Log("drag end " + r);
            r.Intersect(snapshotBounds);
            if (snapshot == null || r.Width < 8 || r.Height < 8)
            {
                box.HideBox();
                return;
            }
            box.ShowRect(r);
            Bitmap crop;
            var src = new Rectangle(r.X - snapshotBounds.X, r.Y - snapshotBounds.Y, r.Width, r.Height);
            crop = snapshot.Clone(src, PixelFormat.Format24bppRgb);
            snapshot.Dispose();
            snapshot = null;

            try
            {
                card = new ActionCard(this, cfg, r, crop);
            }
            finally { crop.Dispose(); }
            card.ShowCard();
        }

        void OnLeftDown(Point p)
        {
            if (card != null && !card.Bounds.Contains(p)) card.Close();
        }

        /// <summary>Runs code on the UI thread from any thread; safe even after a card is closed.</summary>
        public void Post(MethodInvoker action)
        {
            try
            {
                ui.BeginInvoke((MethodInvoker)delegate
                {
                    try { action(); }
                    catch (Exception ex) { Program.Log("post error: " + ex); }
                });
            }
            catch (Exception ex) { Program.Log("post failed: " + ex); }
        }

        public void CardClosed(ActionCard c)
        {
            if (card == c)
            {
                card = null;
                box.HideBox();
            }
        }

        void ToggleEnabled()
        {
            cfg.Enabled = !cfg.Enabled;
            enabledItem.Checked = cfg.Enabled;
            cfg.Save();
        }

        public void ShowSettings()
        {
            if (settings != null) { settings.Activate(); return; }
            settings = new SettingsForm(cfg, voice);
            settings.FormClosed += delegate
            {
                settings = null;
                if (cfg.ClipboardHistory) clips.Start(); else clips.Stop();
                hook.ClearCache();
                enabledItem.Checked = cfg.Enabled;
                if (!voice.ApplyTrigger())
                    MessageBox.Show("A tecla " + VoiceTrigger.Describe(cfg) + " já é usada por outro programa. Escolha outra nas Configurações.",
                        Brand.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            settings.Show();
            Native.ForceForeground(settings.Handle);
        }

        ClipboardHistory clips;
        HistoryForm historyForm;

        public void ShowHistory(Point anchor)
        {
            if (historyForm != null && !historyForm.IsDisposed) { historyForm.Activate(); return; }
            historyForm = new HistoryForm(clips, anchor);
            historyForm.FormClosed += delegate { historyForm = null; };
            historyForm.Show();
            Native.ForceForeground(historyForm.Handle);
            historyForm.Activate();
        }

        WelcomeForm welcome;

        public void ShowWelcome(bool firstRun)
        {
            if (welcome != null) { welcome.Activate(); return; }
            welcome = new WelcomeForm(cfg, voice, firstRun);
            welcome.FormClosed += delegate { welcome = null; };
            welcome.Show();
            Native.ForceForeground(welcome.Handle);
        }

        protected override void ExitThreadCore()
        {
            hook.Dispose();
            clips.Dispose(); // also saves the text history
            voice.Dispose();
            if (card != null) card.Close();
            tray.Visible = false;
            tray.Dispose();
            box.Dispose();
            base.ExitThreadCore();
        }
    }
}
