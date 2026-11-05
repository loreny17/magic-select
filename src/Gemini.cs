using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace MagicSelect
{
    class Turn
    {
        public string Role;      // "user" or "model"
        public string Text;
        public string ImageB64;  // attached file (base64), only on the first user turn
        public string Mime = "image/png";
    }

    class GeminiException : Exception
    {
        public int Status;
        public GeminiException(int status, string message) : base(message) { Status = status; }
    }

    static class Gemini
    {
        const string FallbackModel = "gemini-2.5-flash";

        /// <summary>Streams the answer; onText is called (on a worker thread) for each chunk.</summary>
        public static void Stream(string apiKey, string model, string system, List<Turn> turns, Action<string> onText, bool json = false)
        {
            bool gotText = false;
            Action<string> wrapped = delegate(string t) { gotText = true; onText(t); };
            try
            {
                DoStream(apiKey, model, system, turns, wrapped, json);
            }
            catch (GeminiException ex)
            {
                // Model name unknown (renamed/retired): retry once with a known one.
                if (ex.Status == 404 && !gotText && model != FallbackModel)
                    DoStream(apiKey, FallbackModel, system, turns, wrapped, json);
                else throw;
            }
        }

        /// <summary>Returns null when the key works, otherwise a friendly error message.</summary>
        public static string TestKey(string apiKey, string model)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return "Cole a chave primeiro.";
            try
            {
                var turns = new List<Turn> { new Turn { Role = "user", Text = "Responda apenas: OK" } };
                Ask(apiKey.Trim(), model, "Teste de conexão.", turns);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static string Ask(string apiKey, string model, string system, List<Turn> turns, bool json = false)
        {
            var sb = new StringBuilder();
            Stream(apiKey, model, system, turns, delegate(string t) { sb.Append(t); }, json);
            return sb.ToString().Trim();
        }

        static void DoStream(string apiKey, string model, string system, List<Turn> turns, Action<string> onText, bool json)
        {
            string url = "https://generativelanguage.googleapis.com/v1beta/models/" +
                         Uri.EscapeDataString(model.Trim()) + ":streamGenerateContent?alt=sse";
            byte[] body = Encoding.UTF8.GetBytes(BuildBody(system, turns, json));

            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Headers.Add("x-goog-api-key", apiKey);
            req.Timeout = 60000;
            req.ReadWriteTimeout = 120000;
            req.ContentLength = body.Length;

            HttpWebResponse resp;
            try
            {
                using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                resp = (HttpWebResponse)req.GetResponse();
            }
            catch (WebException we)
            {
                var r = we.Response as HttpWebResponse;
                if (r == null) throw new GeminiException(0, "Sem conexão com o Gemini: " + we.Message);
                string txt;
                int code = (int)r.StatusCode; // read before the response is disposed
                using (r) using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) txt = sr.ReadToEnd();
                throw new GeminiException(code, FriendlyError(code, txt));
            }

            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            using (resp)
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (!line.StartsWith("data:")) continue;
                    string payload = line.Substring(5).Trim();
                    if (payload.Length == 0) continue;
                    object obj = ser.DeserializeObject(payload);

                    string blocked = Str(Get(Get(obj, "promptFeedback"), "blockReason"));
                    if (blocked != null) throw new GeminiException(200, "O Gemini se recusou a responder (" + blocked + ").");

                    string text = ExtractText(obj);
                    if (!string.IsNullOrEmpty(text)) onText(text);
                }
            }
        }

        static string BuildBody(string system, List<Turn> turns, bool json)
        {
            var contents = new List<object>();
            foreach (var t in turns)
            {
                var parts = new List<object>();
                if (t.ImageB64 != null)
                {
                    parts.Add(new Dictionary<string, object> {
                        { "inline_data", new Dictionary<string, object> { { "mime_type", t.Mime }, { "data", t.ImageB64 } } }
                    });
                }
                if (!string.IsNullOrEmpty(t.Text))
                    parts.Add(new Dictionary<string, object> { { "text", t.Text } });
                contents.Add(new Dictionary<string, object> { { "role", t.Role }, { "parts", parts } });
            }
            var root = new Dictionary<string, object> {
                { "system_instruction", new Dictionary<string, object> {
                    { "parts", new List<object> { new Dictionary<string, object> { { "text", system } } } } } },
                { "contents", contents }
            };
            if (json)
                root["generationConfig"] = new Dictionary<string, object> { { "responseMimeType", "application/json" } };
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            return ser.Serialize(root);
        }

        static string ExtractText(object obj)
        {
            var cands = Get(obj, "candidates") as IList;
            if (cands == null || cands.Count == 0) return null;
            var parts = Get(Get(cands[0], "content"), "parts") as IList;
            if (parts == null) return null;
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                if (Get(p, "thought") is bool && (bool)Get(p, "thought")) continue;
                string t = Str(Get(p, "text"));
                if (t != null) sb.Append(t);
            }
            return sb.ToString();
        }

        static string FriendlyError(int status, string body)
        {
            string msg = null;
            try { msg = Str(Get(Get(new JavaScriptSerializer().DeserializeObject(body), "error"), "message")); } catch { }
            if (msg == null) msg = "HTTP " + status;
            if (status == 400 && msg.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Chave de API inválida. Confira nas Configurações.";
            if (status == 403) return "Acesso negado pela chave de API: " + msg;
            if (status == 429) return "Limite de uso do Gemini atingido. Espere um pouco e tente de novo.";
            if (status == 404) return "Modelo não encontrado: " + msg;
            return msg;
        }

        static object Get(object o, string key)
        {
            var d = o as IDictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        static string Str(object o) { return o as string; }
    }
}
