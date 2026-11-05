using System;
using System.Drawing;
using System.Windows.Forms;

namespace MagicSelect
{
    /// <summary>Names and rules for the microphone button.</summary>
    static class VoiceTrigger
    {
        public const int ModAlt = 1, ModCtrl = 2, ModShift = 4, ModWin = 8;

        public static string Describe(Config c)
        {
            return Describe(c.VoiceKey, c.VoiceModifiers, c.VoiceMouse);
        }

        public static string Describe(int vk, int mods, string mouse)
        {
            if (!string.IsNullOrEmpty(mouse))
            {
                if (mouse == "Middle") return "Botão do meio do mouse (rodinha)";
                if (mouse == "X1") return "Botão lateral do mouse (voltar)";
                if (mouse == "X2") return "Botão lateral do mouse (avançar)";
                return mouse;
            }
            if (vk == 0) return "(nenhum)";
            string s = "";
            if ((mods & ModCtrl) != 0) s += "Ctrl + ";
            if ((mods & ModShift) != 0) s += "Shift + ";
            if ((mods & ModAlt) != 0) s += "Alt + ";
            if ((mods & ModWin) != 0) s += "Win + ";
            return s + KeyName((Keys)vk);
        }

        static string KeyName(Keys k)
        {
            switch (k)
            {
                case Keys.Space: return "Espaço";
                case Keys.Insert: return "Insert";
                case Keys.Pause: return "Pause";
                case Keys.Scroll: return "Scroll Lock";
                case Keys.CapsLock: return "Caps Lock";
                case Keys.Apps: return "Menu";
                case Keys.Oemtilde: return "'";
                case Keys.NumPad0: case Keys.NumPad1: case Keys.NumPad2: case Keys.NumPad3: case Keys.NumPad4:
                case Keys.NumPad5: case Keys.NumPad6: case Keys.NumPad7: case Keys.NumPad8: case Keys.NumPad9:
                    return "Num " + ((int)k - (int)Keys.NumPad0);
            }
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)k - (int)Keys.D0).ToString();
            return k.ToString();
        }

        /// <summary>Keys that would break normal typing if used alone.</summary>
        public static bool NeedsModifier(Keys k)
        {
            return (k >= Keys.A && k <= Keys.Z) || (k >= Keys.D0 && k <= Keys.D9) ||
                   k == Keys.Space || k == Keys.Enter || k == Keys.Back || k == Keys.Tab ||
                   k == Keys.Delete || k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down ||
                   (k >= Keys.Oem1 && k <= Keys.Oem102);
        }
    }

    /// <summary>"Aperte o botão que você quer usar" dialog.</summary>
    class ButtonCaptureForm : Form
    {
        readonly Label info, hint;
        public int Key;
        public int Modifiers;
        public string Mouse = "";

        public ButtonCaptureForm()
        {
            Text = "Configurar botão do microfone";
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            TopMost = true;
            KeyPreview = true;
            ClientSize = new Size(460, 170);
            BackColor = Theme.Bg;

            info = new Label
            {
                Text = "🎤  Aperte agora a tecla ou o botão do mouse\nque vai ligar o microfone",
                Font = new Font("Segoe UI", 13f),
                ForeColor = Theme.Text,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            hint = new Label
            {
                Text = "Pode ser uma tecla (ex.: F9, Ctrl + Espaço) ou um botão lateral / do meio do mouse.  Esc cancela.",
                ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Bottom,
                Height = 48
            };
            Controls.Add(info);
            Controls.Add(hint);
            info.MouseDown += OnMouse;
            hint.MouseDown += OnMouse;
            MouseDown += OnMouse;
        }

        // Let Tab, arrows, Alt combinations etc. reach OnKeyDown instead of moving focus / opening menus.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
            if (msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN)
            {
                HandleKey(keyData & Keys.KeyCode, keyData & Keys.Modifiers);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void HandleKey(Keys key, Keys mods)
        {
            if (key == Keys.Escape && mods == Keys.None) { DialogResult = DialogResult.Cancel; return; }
            if (key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu ||
                key == Keys.LWin || key == Keys.RWin || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                key == Keys.LControlKey || key == Keys.RControlKey || key == Keys.LMenu || key == Keys.RMenu)
                return; // wait for the real key

            int m = 0;
            if ((mods & Keys.Control) != 0) m |= VoiceTrigger.ModCtrl;
            if ((mods & Keys.Shift) != 0) m |= VoiceTrigger.ModShift;
            if ((mods & Keys.Alt) != 0) m |= VoiceTrigger.ModAlt;

            if (m == 0 && VoiceTrigger.NeedsModifier(key))
            {
                Warn("\"" + VoiceTrigger.Describe((int)key, 0, "") + "\" sozinha atrapalharia a digitação.\nUse junto com Ctrl, Alt ou Shift — ou uma tecla como F9.");
                return;
            }
            Key = (int)key;
            Modifiers = m;
            Mouse = "";
            DialogResult = DialogResult.OK;
        }

        void OnMouse(object sender, MouseEventArgs e)
        {
            string b = null;
            if (e.Button == MouseButtons.Middle) b = "Middle";
            else if (e.Button == MouseButtons.XButton1) b = "X1";
            else if (e.Button == MouseButtons.XButton2) b = "X2";
            if (b == null)
            {
                Warn("Os botões esquerdo e direito não podem ser usados.\nUse um botão lateral, o do meio, ou uma tecla.");
                return;
            }
            Key = 0;
            Modifiers = 0;
            Mouse = b;
            DialogResult = DialogResult.OK;
        }

        void Warn(string text)
        {
            info.Text = text;
            info.ForeColor = Theme.Error;
        }
    }
}
