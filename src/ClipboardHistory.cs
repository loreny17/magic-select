using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MagicSelect
{
    enum ClipKind { Text, Image, Files }

    class ClipItem
    {
        public ClipKind Kind;
        public string Text = "";
        public string[] Files;
        public byte[] Png;       // images only (kept in memory, not saved to disk)
        public string Hash;      // images only, to avoid duplicates
        public Bitmap Thumb;     // images only
        public DateTime Time;
    }

    /// <summary>
    /// Remembers everything copied on the computer (text, images, files).
    /// Respects the "don't keep in history" flags that password managers set.
    /// Text history is saved encrypted (DPAPI) so it survives a restart; images stay in memory.
    /// </summary>
    class ClipboardHistory : NativeWindow, IDisposable
    {
        [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        const int WM_CLIPBOARDUPDATE = 0x031D;

        const int MaxText = 200, MaxImages = 30, MaxChars = 20000;

        readonly List<ClipItem> items = new List<ClipItem>();
        readonly Timer readTimer = new Timer();
        readonly Timer saveTimer = new Timer();
        bool listening;
        int retries;

        public event EventHandler Changed;

        public List<ClipItem> Items { get { return items; } }

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"MagicSelect\history.dat"); }
        }

        public ClipboardHistory()
        {
            CreateHandle(new CreateParams());
            // Read a moment after the change: lets the source app finish writing and avoids a locked clipboard.
            readTimer.Interval = 80;
            readTimer.Tick += delegate { Capture(); };
            saveTimer.Interval = 1500;
            saveTimer.Tick += delegate { saveTimer.Stop(); Save(); };
            Load();
        }

        public void Start()
        {
            if (!listening) listening = AddClipboardFormatListener(Handle);
        }

        public void Stop()
        {
            if (listening) RemoveClipboardFormatListener(Handle);
            listening = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                retries = 0;
                readTimer.Stop();
                readTimer.Start();
            }
            base.WndProc(ref m);
        }

        void Capture()
        {
            readTimer.Stop();
            IDataObject data;
            try { data = Clipboard.GetDataObject(); }
            catch
            {
                if (++retries < 6) readTimer.Start(); // clipboard busy: try again shortly
                return;
            }
            if (data == null) return;

            try
            {
                // Password managers and private content ask clipboard tools not to keep it.
                if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") ||
                    data.GetDataPresent("Clipboard Viewer Ignore")) return;
                if (data.GetDataPresent("CanIncludeInClipboardHistory") && !FlagIsTrue(data.GetData("CanIncludeInClipboardHistory"))) return;

                ClipItem item = null;
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null && files.Length > 0)
                        item = new ClipItem { Kind = ClipKind.Files, Files = files, Text = string.Join("\n", files) };
                }
                else if (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text))
                {
                    string t = data.GetData(DataFormats.UnicodeText) as string ?? data.GetData(DataFormats.Text) as string;
                    if (!string.IsNullOrWhiteSpace(t))
                        item = new ClipItem { Kind = ClipKind.Text, Text = t.Length > MaxChars ? t.Substring(0, MaxChars) : t };
                }
                else if (data.GetDataPresent(DataFormats.Bitmap))
                {
                    var img = data.GetData(DataFormats.Bitmap) as Image;
                    if (img != null) item = FromImage(img);
                }

                if (item == null) return;
                item.Time = DateTime.Now;
                Add(item);
            }
            catch (Exception ex) { Program.Log("clipboard read error: " + ex.Message); }
        }

        static bool FlagIsTrue(object o)
        {
            var ms = o as MemoryStream;
            if (ms == null || ms.Length < 4) return true;
            var b = new byte[4];
            ms.Position = 0;
            ms.Read(b, 0, 4);
            return BitConverter.ToInt32(b, 0) != 0;
        }

        static ClipItem FromImage(Image img)
        {
            byte[] png;
            using (var ms = new MemoryStream()) { img.Save(ms, ImageFormat.Png); png = ms.ToArray(); }
            string hash;
            using (var sha = SHA1.Create()) hash = Convert.ToBase64String(sha.ComputeHash(png));

            // small thumbnail for the list
            const int maxW = 360, maxH = 160;
            float k = Math.Min(1f, Math.Min((float)maxW / img.Width, (float)maxH / img.Height));
            var thumb = new Bitmap(Math.Max(1, (int)(img.Width * k)), Math.Max(1, (int)(img.Height * k)));
            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, 0, 0, thumb.Width, thumb.Height);
            }
            return new ClipItem { Kind = ClipKind.Image, Png = png, Hash = hash, Thumb = thumb, Text = img.Width + " × " + img.Height };
        }

        void Add(ClipItem item)
        {
            // Copying something that is already in the list just moves it to the top.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var x = items[i];
                bool same = x.Kind == item.Kind &&
                            (item.Kind == ClipKind.Image ? x.Hash == item.Hash : x.Text == item.Text);
                if (same) { DisposeItem(x); items.RemoveAt(i); }
            }
            items.Insert(0, item);

            int texts = 0, images = 0;
            for (int i = 0; i < items.Count; i++)
            {
                bool over = items[i].Kind == ClipKind.Image ? ++images > MaxImages : ++texts > MaxText;
                if (over) { DisposeItem(items[i]); items.RemoveAt(i); i--; }
            }
            OnChanged();
        }

        public void Remove(ClipItem item)
        {
            if (items.Remove(item)) { DisposeItem(item); OnChanged(); }
        }

        public void Clear()
        {
            foreach (var x in items) DisposeItem(x);
            items.Clear();
            OnChanged();
            Save();
        }

        static void DisposeItem(ClipItem x)
        {
            if (x.Thumb != null) { x.Thumb.Dispose(); x.Thumb = null; }
        }

        void OnChanged()
        {
            saveTimer.Stop();
            saveTimer.Start();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        /// <summary>Puts an item back on the clipboard (it then moves to the top of the list).</summary>
        public bool CopyBack(ClipItem item)
        {
            try
            {
                switch (item.Kind)
                {
                    case ClipKind.Text:
                        Clipboard.SetDataObject(item.Text, true, 10, 100);
                        break;
                    case ClipKind.Files:
                        var list = new StringCollection();
                        list.AddRange(item.Files);
                        var d = new DataObject();
                        d.SetFileDropList(list);
                        Clipboard.SetDataObject(d, true, 10, 100);
                        break;
                    case ClipKind.Image:
                        using (var ms = new MemoryStream(item.Png))
                        using (var bmp = new Bitmap(ms))
                            Clipboard.SetDataObject(new DataObject(DataFormats.Bitmap, new Bitmap(bmp)), true, 10, 100);
                        break;
                }
                return true;
            }
            catch (Exception ex)
            {
                Program.Log("copy back error: " + ex.Message);
                return false;
            }
        }

        // ---------- saved text history (encrypted for the current Windows user) ----------

        public void Save()
        {
            try
            {
                var rows = new List<object>();
                foreach (var x in items)
                {
                    if (x.Kind == ClipKind.Image) continue;
                    rows.Add(new Dictionary<string, object> {
                        { "k", (int)x.Kind }, { "t", x.Text }, { "f", x.Files }, { "d", x.Time.ToString("o") } });
                }
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                byte[] plain = Encoding.UTF8.GetBytes(ser.Serialize(rows));
                byte[] enc = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllBytes(FilePath, enc);
            }
            catch (Exception ex) { Program.Log("history save error: " + ex.Message); }
        }

        void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var rows = ser.DeserializeObject(Encoding.UTF8.GetString(plain)) as object[];
                if (rows == null) return;
                foreach (var r in rows)
                {
                    var d = r as Dictionary<string, object>;
                    if (d == null) continue;
                    var item = new ClipItem { Kind = (ClipKind)Convert.ToInt32(d["k"]), Text = d["t"] as string ?? "" };
                    var files = d.ContainsKey("f") ? d["f"] as object[] : null;
                    if (files != null) item.Files = Array.ConvertAll(files, f => Convert.ToString(f));
                    DateTime t;
                    item.Time = DateTime.TryParse(d["d"] as string, null, System.Globalization.DateTimeStyles.RoundtripKind, out t) ? t : DateTime.Now;
                    items.Add(item);
                }
            }
            catch (Exception ex) { Program.Log("history load error: " + ex.Message); }
        }

        public void Dispose()
        {
            Stop();
            saveTimer.Stop();
            Save();
            readTimer.Dispose();
            saveTimer.Dispose();
            DestroyHandle();
        }
    }
}
