using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MagicSelect
{
    // Read aloud + smart actions.
    partial class ActionCard
    {
        static readonly CultureInfo PtBr = new CultureInfo("pt-BR");

        const string TranscribePrompt =
            "Transcreva exatamente o texto visível na imagem, preservando as quebras de linha e o idioma original. " +
            "Responda somente com o texto, sem comentários nem formatação Markdown. Se não houver texto, responda apenas: (sem texto)";

        string readText;

        // ---------- Histórico de cópias ----------

        void OpenHistory()
        {
            Point at = Location;
            Close();
            app.ShowHistory(at);
        }

        // ---------- Ler em voz alta ----------

        void ReadAloud()
        {
            if (synth != null && synth.State == SynthesizerState.Speaking)
            {
                synth.SpeakAsyncCancelAll();
                return;
            }
            if (readText != null) { Speak(readText); return; }
            if (busy) return;
            Run("Ler em voz alta", TranscribePrompt, false, delegate(string text)
            {
                if (text.Length == 0 || text == "(sem texto)")
                {
                    if (!IsDisposed) status.Text = "Não há texto para ler";
                    return;
                }
                readText = text;
                if (!IsDisposed) Speak(text);
            });
        }

        void Speak(string text)
        {
            try
            {
                if (synth == null)
                {
                    synth = new SpeechSynthesizer();
                    synth.SetOutputToDefaultAudioDevice();
                    synth.SpeakStarted += delegate { app.Post(delegate { SetSpeaking(true); }); };
                    synth.SpeakCompleted += delegate { app.Post(delegate { SetSpeaking(false); }); };
                }
                PickVoice(GuessLanguage(text));
                synth.SpeakAsync(text.Replace("*", "").Replace("#", ""));
            }
            catch (Exception ex)
            {
                Program.Log("speech error: " + ex);
                status.Text = "Não consegui ler em voz alta";
            }
        }

        void SetSpeaking(bool speaking)
        {
            if (IsDisposed) return;
            speakBtn.Icon = speaking ? IconStop : IconSpeak;
            tips.SetToolTip(speakBtn, speaking ? "Parar leitura" : "Ler em voz alta");
            speakBtn.Invalidate();
            status.Text = speaking ? "Lendo… 🔊" : "";
        }

        void PickVoice(string lang)
        {
            string fallback = null;
            foreach (InstalledVoice v in synth.GetInstalledVoices())
            {
                if (!v.Enabled) continue;
                string c = v.VoiceInfo.Culture.Name;
                if (c.StartsWith(lang, StringComparison.OrdinalIgnoreCase)) { synth.SelectVoice(v.VoiceInfo.Name); return; }
                if (fallback == null && c.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) fallback = v.VoiceInfo.Name;
            }
            if (fallback != null) synth.SelectVoice(fallback);
        }

        /// <summary>Rough language guess from common words, so English text is read by an English voice.</summary>
        static string GuessLanguage(string text)
        {
            string t = " " + text.ToLowerInvariant().Replace("\n", " ").Replace(",", " ").Replace(".", " ") + " ";
            int pt = Count(t, " não ", " que ", " para ", " com ", "ção", "ões", " é ", " uma ", " você ", " do ", " da ", "ã");
            int en = Count(t, " the ", " and ", " is ", " you ", " to ", " of ", " with ", " for ", " this ", " that ", " are ");
            int es = Count(t, " el ", " los ", " las ", " es ", " por ", " pero ", "ñ", " una ", " está ", " muy ", " y ");
            if (en > pt && en >= es) return "en";
            if (es > pt && es > en) return "es";
            return "pt";
        }

        static int Count(string text, params string[] words)
        {
            int n = 0;
            foreach (string w in words)
                for (int i = text.IndexOf(w, StringComparison.Ordinal); i >= 0; i = text.IndexOf(w, i + 1, StringComparison.Ordinal)) n++;
            return n;
        }

        // ---------- Ações inteligentes ----------

        class SmartAction
        {
            public string Icon, Label, Url, Copy;
        }

        const string SmartPrompt =
            "Analise a imagem (uma área que o usuário selecionou na tela) e encontre itens para ação rápida. " +
            "Responda SOMENTE JSON no formato {\"actions\": [...]}, com no máximo 3 itens, cada um sendo um destes:\n" +
            "{\"type\":\"maps\",\"value\":\"endereço ou nome de lugar, completo\"}\n" +
            "{\"type\":\"link\",\"value\":\"URL\"}\n" +
            "{\"type\":\"email\",\"value\":\"endereço de e-mail\"}\n" +
            "{\"type\":\"phone\",\"value\":\"número de telefone\"}\n" +
            "{\"type\":\"calc\",\"value\":\"expressão com + - * / e parênteses, ponto como decimal, sem separador de milhar\"}\n" +
            "{\"type\":\"currency\",\"amount\":número,\"from\":\"código ISO da moeda, ex.: USD\"}\n" +
            "Regras: só inclua o que estiver claramente visível. calc só para contas explícitas (ex.: '12 x 7', '15% de 200' vira 200*0.15). " +
            "currency só para valores em moeda estrangeira (não real). Se não houver nada útil, responda {\"actions\": []}.";

        /// <summary>Looks at the selection in the background; chips appear only if something useful is found.</summary>
        void StartSmartActions()
        {
            if (!cfg.SmartActions || string.IsNullOrEmpty(cfg.ApiKey)) return;
            string key = cfg.ApiKey, model = cfg.Model;
            var turns = new List<Turn> { new Turn { Role = "user", ImageB64 = imageB64, Text = SmartPrompt } };
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string json = Gemini.Ask(key, model, "Você extrai ações rápidas de imagens. Responda só JSON.", turns, true);
                    Program.Log("smart json: " + json);
                    List<SmartAction> found = ParseSmart(json);
                    if (found.Count == 0) return;
                    app.Post(delegate
                    {
                        if (IsDisposed) return;
                        foreach (var a in found) AddSmartChip(a);
                        DoLayout();
                    });
                }
                catch (Exception ex) { Program.Log("smart error: " + ex); }
            });
        }

        void AddSmartChip(SmartAction a)
        {
            var c = new PillButton();
            c.Icon = a.Icon;
            c.IconFont = iconFont;
            c.Text = a.Label;
            c.Font = smallFont;
            c.Outline = true;
            if (a.Url != null) tips.SetToolTip(c, a.Url);
            c.Click += delegate
            {
                if (a.Copy != null)
                {
                    if (SetClipboard(a.Copy)) { c.Text = "Copiado ✓  " + a.Copy; DoLayout(); }
                }
                else if (a.Url != null)
                {
                    OpenUrl(a.Url);
                    Close();
                }
            };
            smart.Add(c);
            Controls.Add(c);
        }

        static List<SmartAction> ParseSmart(string json)
        {
            var list = new List<SmartAction>();
            json = json.Trim().Trim('`');
            if (json.StartsWith("json")) json = json.Substring(4);
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            object arrObj;
            if (root == null || !root.TryGetValue("actions", out arrObj)) return list;
            var arr = arrObj as IList;
            if (arr == null) return list;

            foreach (object o in arr)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                SmartAction a = null;
                try { a = Build(d); }
                catch (Exception ex) { Program.Log("smart item skipped: " + ex.Message); }
                if (a == null) continue;
                bool dup = false;
                foreach (var x in list) if (x.Label == a.Label) dup = true;
                if (!dup) list.Add(a);
                if (list.Count == 3) break;
            }
            return list;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture).Trim() : "";
        }

        static string Short(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        static SmartAction Build(Dictionary<string, object> d)
        {
            string type = Str(d, "type").ToLowerInvariant();
            string value = Str(d, "value");
            switch (type)
            {
                case "maps":
                    if (value.Length < 4) return null;
                    return new SmartAction { Icon = "", Label = "Ver no Maps",
                        Url = "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(value) };

                case "link":
                {
                    string url = value.StartsWith("http://") || value.StartsWith("https://") ? value : "https://" + value;
                    Uri u;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Host.IndexOf('.') < 0) return null;
                    string host = u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host;
                    return new SmartAction { Icon = "", Label = "Abrir " + Short(host, 28), Url = u.AbsoluteUri };
                }

                case "email":
                    if (value.IndexOf('@') < 1) return null;
                    return new SmartAction { Icon = "", Label = "E-mail para " + Short(value, 26), Url = "mailto:" + value };

                case "phone":
                {
                    var digits = new StringBuilder();
                    foreach (char ch in value) if (char.IsDigit(ch)) digits.Append(ch);
                    string n = digits.ToString();
                    if (n.Length == 10 || n.Length == 11) n = "55" + n; // Brazilian number without country code
                    if (n.Length < 11) return null;
                    return new SmartAction { Icon = "", Label = "WhatsApp " + Short(value, 20), Url = "https://wa.me/" + n };
                }

                case "calc":
                {
                    string expr = value.Replace("×", "*").Replace("x", "*").Replace("X", "*").Replace("÷", "/");
                    foreach (char ch in expr)
                        if ("0123456789.+-*/() ".IndexOf(ch) < 0) return null; // only plain arithmetic
                    double r = Convert.ToDouble(new System.Data.DataTable().Compute(expr, null), CultureInfo.InvariantCulture);
                    if (double.IsNaN(r) || double.IsInfinity(r)) return null;
                    string result = r.ToString("#,##0.####", PtBr);
                    string pretty = expr.Replace(" ", "").Replace("*", " × ").Replace("/", " ÷ ").Replace("+", " + ").Replace("-", " − ").Replace(".", ",");
                    return new SmartAction { Icon = "", Label = Short(pretty, 24) + " = " + result, Copy = result };
                }

                case "currency":
                {
                    string from = Str(d, "from").ToUpperInvariant();
                    double amount;
                    if (from.Length != 3 || from == "BRL" ||
                        !double.TryParse(Str(d, "amount"), NumberStyles.Float, CultureInfo.InvariantCulture, out amount)) return null;
                    double rate = RateToBrl(from);
                    if (rate <= 0) return null;
                    string brl = (amount * rate).ToString("N2", PtBr);
                    return new SmartAction { Icon = "", Label = Money(amount, from) + " ≈ R$ " + brl, Copy = brl };
                }
            }
            return null;
        }

        static string Money(double amount, string code)
        {
            string sym = code == "USD" ? "US$ " : code == "EUR" ? "€ " : code == "GBP" ? "£ " : code == "JPY" ? "¥ " : code + " ";
            return sym + amount.ToString("N2", PtBr);
        }

        /// <summary>Today's exchange rate (free, no key needed).</summary>
        static double RateToBrl(string from)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://open.er-api.com/v6/latest/" + from);
                req.Timeout = 6000;
                string body;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    body = sr.ReadToEnd();
                var root = new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string, object>;
                object rates;
                if (root == null || !root.TryGetValue("rates", out rates)) return 0;
                var r = rates as Dictionary<string, object>;
                object brl;
                if (r == null || !r.TryGetValue("BRL", out brl)) return 0;
                return Convert.ToDouble(brl, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Program.Log("rate error: " + ex.Message);
                return 0;
            }
        }
    }
}
