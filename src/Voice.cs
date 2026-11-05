using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MagicSelect
{
    /// <summary>Records the default microphone to a WAV file using the built-in Windows MCI API.</summary>
    class MicRecorder
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder ret, int retLen, IntPtr callback);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern bool mciGetErrorString(int error, StringBuilder text, int len);

        const string Alias = "magicselect_rec";
        public bool Recording { get; private set; }

        static void Mci(string cmd)
        {
            int err = mciSendString(cmd, null, 0, IntPtr.Zero);
            if (err != 0)
            {
                var sb = new StringBuilder(256);
                mciGetErrorString(err, sb, sb.Capacity);
                throw new InvalidOperationException("Microfone: " + sb);
            }
        }

        public void Start()
        {
            mciSendString("close " + Alias, null, 0, IntPtr.Zero);
            Mci("open new type waveaudio alias " + Alias);
            try
            {
                Mci("set " + Alias + " time format ms bitspersample 16 channels 1 samplespersec 16000 bytespersec 32000 alignment 2");
                Mci("record " + Alias);
            }
            catch
            {
                mciSendString("close " + Alias, null, 0, IntPtr.Zero);
                throw;
            }
            Recording = true;
        }

        /// <summary>Stops and returns the WAV bytes.</summary>
        public byte[] Stop()
        {
            if (!Recording) return null;
            Recording = false;
            string path = Path.Combine(Path.GetTempPath(), "magicselect_voice.wav");
            try
            {
                Mci("stop " + Alias);
                Mci("save " + Alias + " \"" + path + "\"");
            }
            finally
            {
                mciSendString("close " + Alias, null, 0, IntPtr.Zero);
            }
            byte[] data = File.ReadAllBytes(path);
            try { File.Delete(path); } catch { }
            return data;
        }

        public void Cancel()
        {
            if (!Recording) return;
            Recording = false;
            mciSendString("close " + Alias, null, 0, IntPtr.Zero);
        }
    }

    /// <summary>Small bubble at the bottom of the screen: "Ouvindo…", "Entendendo…", "Abrindo…".</summary>
    class VoiceBubble : Form
    {
        readonly Label label;
        readonly float s;
        public event EventHandler Clicked;

        public VoiceBubble()
        {
            s = Native.ScaleAt(Cursor.Position);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;

            label = new Label();
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.ForeColor = Theme.Text;
            label.Font = new Font("Segoe UI", 15 * s, GraphicsUnit.Pixel);
            label.Click += delegate { if (Clicked != null) Clicked(this, EventArgs.Empty); };
            Controls.Add(label);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= Native.CS_DROPSHADOW;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle, Theme.Accent);
        }

        public void Say(string text, Color color)
        {
            label.Text = text;
            label.ForeColor = color;
            Size t = TextRenderer.MeasureText(text, label.Font);
            int w = Math.Min(t.Width + (int)(48 * s), (int)(720 * s));
            int h = (int)(52 * s);
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            SetBounds(wa.Left + (wa.Width - w) / 2, wa.Bottom - h - (int)(40 * s), w, h);
            if (!Visible) Show();
            Refresh();
        }
    }

    /// <summary>
    /// Push-to-talk voice commands. Hold Ctrl+Shift+Space (or click the tray icon) and say e.g.
    /// "toca Evidências no YouTube": Gemini understands it and the video opens.
    /// </summary>
    class VoiceCommands : IDisposable
    {
        string HotkeyText { get { return VoiceTrigger.Describe(cfg); } }

        readonly App app;
        readonly Config cfg;
        readonly MicRecorder mic = new MicRecorder();
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer();
        readonly HotkeyWindow hotkey;
        VoiceBubble bubble;
        DateTime startedAt;
        bool holdMode;
        bool working;

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vk);

        bool suspended;

        public VoiceCommands(App app, Config cfg)
        {
            this.app = app;
            this.cfg = cfg;
            poll.Interval = 50;
            poll.Tick += OnPoll;
            hideTimer.Tick += delegate { hideTimer.Stop(); HideBubble(); };
            hotkey = new HotkeyWindow();
            hotkey.Pressed += delegate { TriggerDown(); };
            ApplyTrigger();
        }

        /// <summary>(Re)registers the configured key. Returns false if another program already uses it.</summary>
        public bool ApplyTrigger()
        {
            suspended = false;
            hotkey.Unregister();
            if (cfg.VoiceMouse.Length > 0 || cfg.VoiceKey == 0) return true; // mouse buttons go through the mouse hook
            bool ok = hotkey.Register(cfg.VoiceKey, cfg.VoiceModifiers);
            if (!ok) Program.Log("hotkey " + HotkeyText + " is taken by another program");
            return ok;
        }

        /// <summary>Turns the trigger off while the user is picking a new one.</summary>
        public void Suspend()
        {
            suspended = true;
            hotkey.Unregister();
        }

        /// <summary>Called by the mouse hook for middle/side buttons. Returns true to swallow the click.</summary>
        public bool OnMouseButton(string button, bool down)
        {
            if (suspended || cfg.VoiceMouse != button) return false;
            if (down) app.Post(TriggerDown); else app.Post(TriggerUp);
            return true;
        }

        void TriggerDown()
        {
            if (mic.Recording)
            {
                if (!holdMode) Finish(); // second press in tap mode = send
                return;
            }
            Begin(true);
        }

        void TriggerUp()
        {
            if (!mic.Recording || !holdMode) return;
            if ((DateTime.Now - startedAt).TotalMilliseconds < 400)
            {
                // Quick tap: keep listening until the button is pressed again.
                holdMode = false;
                Show("🎤  Ouvindo… aperte " + HotkeyText + " de novo para enviar", Theme.Text, 0);
            }
            else Finish();
        }

        /// <summary>Tray icon click: start listening, click again (or on the bubble) to send.</summary>
        public void Toggle()
        {
            if (mic.Recording) Finish();
            else Begin(false);
        }

        void Begin(bool fromHotkey)
        {
            if (working) return;
            if (string.IsNullOrEmpty(cfg.ApiKey))
            {
                Show("Cole sua chave do Gemini nas Configurações para usar a voz", Theme.Error, 4000);
                return;
            }
            try
            {
                mic.Start();
            }
            catch (Exception ex)
            {
                Program.Log("mic error: " + ex);
                Show("Não consegui abrir o microfone. Veja Configurações do Windows › Privacidade › Microfone", Theme.Error, 5000);
                return;
            }
            Program.Log("mic listening (hold=" + fromHotkey + ")");
            AppIndex.RefreshAsync(); // picks up newly installed programs while you speak
            startedAt = DateTime.Now;
            holdMode = fromHotkey;
            hideTimer.Stop();
            Show(fromHotkey ? "🎤  Ouvindo… solte para enviar" : "🎤  Ouvindo… clique aqui para enviar", Theme.Text, 0);
            poll.Start();
        }

        void OnPoll(object sender, EventArgs e)
        {
            double ms = (DateTime.Now - startedAt).TotalMilliseconds;
            if (ms > 15000) { Finish(); return; }
            // Keyboard keys: watch for release here (mouse buttons report release through the hook).
            if (holdMode && cfg.VoiceMouse.Length == 0 && cfg.VoiceKey != 0 &&
                (GetAsyncKeyState(cfg.VoiceKey) & 0x8000) == 0)
                TriggerUp();
        }

        void Finish()
        {
            poll.Stop();
            byte[] wav;
            try { wav = mic.Stop(); }
            catch (Exception ex)
            {
                Program.Log("mic stop error: " + ex);
                Show("Erro ao gravar: " + ex.Message, Theme.Error, 4000);
                return;
            }
            Program.Log("mic stopped, wav bytes=" + (wav == null ? -1 : wav.Length));
            if (wav == null || wav.Length < 16000) // under ~0.5 s of audio
            {
                Show("Não ouvi nada. Segure " + HotkeyText + " enquanto fala.", Theme.Muted, 3000);
                return;
            }
            RunAudio(wav);
        }

        /// <summary>Sends the recorded audio to Gemini and runs the command. Also used by --voice-test.</summary>
        public void RunAudio(byte[] wav, bool dryRun = false)
        {
            working = true;
            Show("✨  Entendendo…", Theme.Accent, 0);
            string key = cfg.ApiKey, model = cfg.Model;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    List<InstalledApp> installed = AppIndex.Snapshot(3000);
                    var cmd = Understand(key, model, wav, installed);
                    Program.Log("voice heard='" + cmd.Heard + "' action=" + cmd.Action + " query='" + cmd.Query + "' url='" + cmd.Url + "' app='" + cmd.App + "'");
                    string url, message;
                    InstalledApp program;
                    Resolve(cmd, installed, out url, out program, out message);
                    Program.Log("voice open " + (program != null ? "app " + program.Name + " [" + program.Id + "]" : url));
                    app.Post(delegate
                    {
                        working = false;
                        if (url == null && program == null) { Show(message, Theme.Muted, 3500); return; }
                        Show(message, Theme.Text, 3000);
                        if (dryRun) return;
                        try
                        {
                            if (program != null) AppIndex.Launch(program);
                            else OpenUrl(url);
                        }
                        catch (Exception ex)
                        {
                            Program.Log("launch error: " + ex);
                            Show("⚠  Não consegui abrir " + (program != null ? program.Name : url), Theme.Error, 4000);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Program.Log("voice error: " + ex);
                    string msg = ex.Message;
                    app.Post(delegate { working = false; Show("⚠  " + msg, Theme.Error, 5000); });
                }
            });
        }

        class Command
        {
            public string Heard = "", Action = "", Query = "", Url = "", App = "";
        }

        static Command Understand(string key, string model, byte[] wav, List<InstalledApp> installed)
        {
            var names = new StringBuilder();
            for (int i = 0; i < installed.Count && i < 600; i++)
            {
                if (i > 0) names.Append(" | ");
                names.Append(installed[i].Name);
            }
            string system =
                "Você interpreta comandos de voz para abrir coisas no computador. Ouça o áudio e responda SOMENTE com JSON no formato " +
                "{\"heard\": \"transcrição do que foi dito\", \"action\": \"app\" | \"youtube\" | \"search\" | \"open\" | \"none\", " +
                "\"app\": \"...\", \"query\": \"...\", \"url\": \"...\"}.\n" +
                "- app: quando pedir para abrir, iniciar ou jogar um programa, jogo ou aplicativo que esteja na lista de programas instalados abaixo. " +
                "app = o nome EXATAMENTE como está na lista (escolha o mais parecido com o que foi falado; ex.: 'abre o zap' = WhatsApp, 'bloco de notas' = Bloco de Notas).\n" +
                "- youtube: quando pedir para tocar, ouvir, ver ou abrir música, clipe, vídeo, playlist ou algo no YouTube. " +
                "query = só o nome da música/vídeo/artista, com a grafia correta (sem palavras como 'toca', 'no YouTube').\n" +
                "- open: quando pedir para abrir um site ou serviço que NÃO está na lista de programas. url = endereço completo com https://.\n" +
                "- search: qualquer outro pedido ou pergunta. query = o que pesquisar no Google.\n" +
                "- none: se não houver fala no áudio.\n\n" +
                "Programas instalados: " + (names.Length > 0 ? names.ToString() : "(lista indisponível)");
            var turns = new List<Turn> { new Turn {
                Role = "user", Mime = "audio/wav", ImageB64 = Convert.ToBase64String(wav),
                Text = "Interprete este comando de voz." } };
            string json = Gemini.Ask(key, model, system, turns, true);
            json = json.Trim().Trim('`');
            if (json.StartsWith("json")) json = json.Substring(4);

            var cmd = new Command();
            var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) return cmd;
            object v;
            if (d.TryGetValue("heard", out v) && v is string) cmd.Heard = ((string)v).Trim();
            if (d.TryGetValue("action", out v) && v is string) cmd.Action = ((string)v).Trim().ToLowerInvariant();
            if (d.TryGetValue("query", out v) && v is string) cmd.Query = ((string)v).Trim();
            if (d.TryGetValue("url", out v) && v is string) cmd.Url = ((string)v).Trim();
            if (d.TryGetValue("app", out v) && v is string) cmd.App = ((string)v).Trim();
            return cmd;
        }

        static void Resolve(Command cmd, List<InstalledApp> installed, out string url, out InstalledApp program, out string message)
        {
            url = null;
            program = null;
            switch (cmd.Action)
            {
                case "app":
                    program = AppIndex.Find(cmd.App, installed);
                    if (program != null)
                    {
                        message = "🚀  Abrindo " + program.Name;
                        return;
                    }
                    // Not installed: fall back to the website or a search.
                    if (cmd.Url.StartsWith("http")) goto case "open";
                    if (cmd.Query.Length == 0) cmd.Query = cmd.App;
                    goto case "search";
                case "youtube":
                    if (cmd.Query.Length == 0) goto default;
                    string id = FirstYouTubeVideo(cmd.Query);
                    url = id != null
                        ? "https://www.youtube.com/watch?v=" + id
                        : "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(cmd.Query);
                    message = "▶  YouTube: " + cmd.Query;
                    return;
                case "open":
                    if (cmd.Url.StartsWith("http://") || cmd.Url.StartsWith("https://"))
                    {
                        url = cmd.Url;
                        message = "🌐  Abrindo " + cmd.Url.Replace("https://", "").Replace("http://", "").TrimEnd('/');
                        return;
                    }
                    goto case "search";
                case "search":
                    string q = cmd.Query.Length > 0 ? cmd.Query : cmd.Heard;
                    if (q.Length == 0) goto default;
                    url = "https://www.google.com/search?q=" + Uri.EscapeDataString(q);
                    message = "🔍  Pesquisando: " + q;
                    return;
                default:
                    message = "Não entendi. Tente de novo falando mais perto do microfone.";
                    return;
            }
        }

        /// <summary>Finds the first video in YouTube's search results so it opens already playing.</summary>
        static string FirstYouTubeVideo(string query)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query));
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
                req.Headers.Add("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.8");
                req.Timeout = 8000;
                string html;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    html = sr.ReadToEnd();
                var m = Regex.Match(html, "\"videoRenderer\":\\{\"videoId\":\"([A-Za-z0-9_-]{11})\"");
                if (!m.Success) m = Regex.Match(html, "\"videoId\":\"([A-Za-z0-9_-]{11})\"");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch (Exception ex)
            {
                Program.Log("youtube lookup failed: " + ex.Message);
                return null;
            }
        }

        void Show(string text, Color color, int hideAfterMs)
        {
            if (bubble == null || bubble.IsDisposed)
            {
                bubble = new VoiceBubble();
                bubble.Clicked += delegate { if (mic.Recording) Finish(); else HideBubble(); };
            }
            bubble.Say(text, color);
            hideTimer.Stop();
            if (hideAfterMs > 0) { hideTimer.Interval = hideAfterMs; hideTimer.Start(); }
        }

        void HideBubble()
        {
            if (bubble != null && !bubble.IsDisposed) bubble.Hide();
        }

        static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        public void Dispose()
        {
            mic.Cancel();
            poll.Dispose();
            hideTimer.Dispose();
            hotkey.Dispose();
            if (bubble != null) bubble.Dispose();
        }
    }

    /// <summary>Receives the global Ctrl+Shift+Space hotkey.</summary>
    class HotkeyWindow : NativeWindow, IDisposable
    {
        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event EventHandler Pressed;
        public bool Registered { get; private set; }

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        /// <summary>modifiers: VoiceTrigger.ModAlt/ModCtrl/ModShift/ModWin flags.</summary>
        public bool Register(int vk, int modifiers)
        {
            Unregister();
            const uint MOD_NOREPEAT = 0x4000;
            Registered = RegisterHotKey(Handle, 1, (uint)modifiers | MOD_NOREPEAT, (uint)vk);
            return Registered;
        }

        public void Unregister()
        {
            if (Registered) UnregisterHotKey(Handle, 1);
            Registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && Pressed != null) Pressed(this, EventArgs.Empty); // WM_HOTKEY
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                Unregister();
                DestroyHandle();
            }
        }
    }
}
