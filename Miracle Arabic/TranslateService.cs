using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Miracle
{
    public readonly record struct AutoTranslation(string Text, string? DetectedLanguage);

    public static class TranslateService
    {
        private static readonly HttpClient Http = CreateClient();

        // Creates the shared HttpClient with a 12 s timeout and a browser-like User-Agent.
        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            return c;
        }

        public static async Task<string?> TranslateAsync(string text, string? from, string? to, string provider = "Google", string? deepLKey = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return text;

            if (string.Equals(provider, "DeepL", StringComparison.OrdinalIgnoreCase))
                return await DeepLAsync(text, from!, to!, deepLKey);

            string? result = await GoogleAsync(text, from!, to!);
            if (!string.IsNullOrEmpty(result)) return result;

            result = await MyMemoryAsync(text, from!, to!);
            return string.IsNullOrEmpty(result) ? null : result;
        }

        public static async Task<AutoTranslation?> TranslateAutoAsync(
            string text, string to, string provider, string? deepLKey, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(to)) return null;

            if (string.Equals(provider, "DeepL", StringComparison.OrdinalIgnoreCase))
                return await DeepLAutoAsync(text, to, deepLKey, token);

            return await GoogleAutoAsync(text, to, token);
        }

        // Auto-detect translation through the Google endpoint, forwarding cancellation; null on any failure.
        private static async Task<AutoTranslation?> GoogleAutoAsync(string text, string to, CancellationToken token)
        {
            try
            {
                string url = "https://clients5.google.com/translate_a/t?client=dict-chrome-ex"
                             + "&sl=auto"
                             + "&tl=" + Uri.EscapeDataString(to)
                             + "&q=" + Uri.EscapeDataString(text);
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseContentRead, token);
                if (!resp.IsSuccessStatusCode) return null;
                string json = await resp.Content.ReadAsStringAsync(token);
                return ExtractGoogleAuto(json);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // HttpClient timeout, not a superseded request.
                return null;
            }
            catch { return null; }
        }

        internal static AutoTranslation? ExtractGoogleAuto(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.String)
                {
                    string? direct = root.GetString();
                    return string.IsNullOrEmpty(direct) ? null : new AutoTranslation(direct, null);
                }
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;

                var sb = new StringBuilder();
                string? detected = null;

                void Take(JsonElement segment)
                {
                    if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0) return;
                    if (segment[0].ValueKind == JsonValueKind.String) sb.Append(segment[0].GetString());
                    if (detected == null && segment.GetArrayLength() > 1
                        && segment[1].ValueKind == JsonValueKind.String)
                        detected = segment[1].GetString();
                }

                var first = root[0];
                if (first.ValueKind == JsonValueKind.String)
                {
                    // ["translated"]
                    sb.Append(first.GetString());
                }
                else if (first.ValueKind == JsonValueKind.Array
                         && first.GetArrayLength() > 0
                         && first[0].ValueKind == JsonValueKind.Array)
                {
                    // Segment shape: the first element is an array of segments, each carrying text and its language.
                    foreach (var segment in first.EnumerateArray()) Take(segment);
                }
                else
                {
                    // [["translated","en"]] - a single segment carrying the text plus the detection.
                    Take(first);
                }

                if (sb.Length == 0) return null;
                return new AutoTranslation(sb.ToString(), detected);
            }
            catch { return null; }
        }

        // Auto-detect translation through DeepL. Returns null when the key is missing or the request fails.
        private static async Task<AutoTranslation?> DeepLAutoAsync(
            string text, string to, string? key, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, DeepLHost(key) + "/v2/translate");
                req.Headers.Add("Authorization", "DeepL-Auth-Key " + key);
                req.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("text", text),
                    new KeyValuePair<string, string>("target_lang", DeepLCode(to, true))
                });
                using var resp = await Http.SendAsync(req, token);
                if (!resp.IsSuccessStatusCode) return null;
                string json = await resp.Content.ReadAsStringAsync(token);
                return ExtractDeepLAuto(json);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch { return null; }
        }

        // Extracts the text and detected language from a DeepL auto-detect response.
        internal static AutoTranslation? ExtractDeepLAuto(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("translations", out var arr)
                    || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
                    return null;

                var first = arr[0];
                if (!first.TryGetProperty("text", out var textEl) || textEl.ValueKind != JsonValueKind.String)
                    return null;

                string? detected = first.TryGetProperty("detected_source_language", out var d)
                                   && d.ValueKind == JsonValueKind.String
                    ? d.GetString()
                    : null;

                string? translated = textEl.GetString();
                return string.IsNullOrEmpty(translated) ? null : new AutoTranslation(translated, detected);
            }
            catch { return null; }
        }

        // Translation through the Google endpoint with explicit source and target languages.
        private static async Task<string?> GoogleAsync(string text, string from, string to)
        {
            try
            {
                string url = "https://clients5.google.com/translate_a/t?client=dict-chrome-ex"
                             + "&sl=" + Uri.EscapeDataString(from)
                             + "&tl=" + Uri.EscapeDataString(to)
                             + "&q=" + Uri.EscapeDataString(text);
                return ExtractGoogle(await Http.GetStringAsync(url));
            }
            catch { return null; }
        }

        // Extracts the translated text from the various Google shapes (plain text, array, or segments).
        private static string? ExtractGoogle(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.String) return root.GetString();
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;

                var first = root[0];
                if (first.ValueKind == JsonValueKind.String) return first.GetString();

                if (first.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var segment in first.EnumerateArray())
                    {
                        if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0) continue;
                        if (segment[0].ValueKind == JsonValueKind.String) sb.Append(segment[0].GetString());
                    }
                    return sb.Length == 0 ? null : sb.ToString();
                }
                return null;
            }
            catch { return null; }
        }

        // Translation through MyMemory, discarding any result that carries a MyMemory warning instead of a translation.
        private static async Task<string?> MyMemoryAsync(string text, string from, string to)
        {
            try
            {
                string url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text)
                             + "&langpair=" + Uri.EscapeDataString(from + "|" + to);
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                if (doc.RootElement.TryGetProperty("responseData", out var rd)
                    && rd.TryGetProperty("translatedText", out var tt)
                    && tt.ValueKind == JsonValueKind.String)
                {
                    string s = tt.GetString()!;
                    if (!string.IsNullOrWhiteSpace(s) && !s.StartsWith("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
                        return s;
                }
                return null;
            }
            catch { return null; }
        }

        // Picks the DeepL host: a key ending in :fx belongs to the free account, anything else to Pro.
        private static string DeepLHost(string key) => key.EndsWith(":fx") ? "https://api-free.deepl.com" : "https://api.deepl.com";

        // Maps a language code to what DeepL accepts; the format differs between the source and target fields.
        private static string DeepLCode(string code, bool target)
        {
            string c = code.ToUpperInvariant();
            if (!target) return c == "ZH-CN" ? "ZH" : c;
            return c switch
            {
                "EN" => "EN-US",
                "PT" => "PT-PT",
                "ZH-CN" => "ZH",
                _ => c
            };
        }

        // Translation through DeepL with two explicit languages. Returns null when the key is missing or the request fails.
        private static async Task<string?> DeepLAsync(string text, string from, string to, string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, DeepLHost(key) + "/v2/translate");
                req.Headers.Add("Authorization", "DeepL-Auth-Key " + key);
                req.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("text", text),
                    new KeyValuePair<string, string>("source_lang", DeepLCode(from, false)),
                    new KeyValuePair<string, string>("target_lang", DeepLCode(to, true))
                });
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;
                string json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("translations", out var arr)
                    && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
                    && arr[0].TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    return t.GetString();
                return null;
            }
            catch { return null; }
        }

        public static async Task<(bool ok, string message)> DeepLCheckAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return (false, "No key");
            string[] hosts = key.EndsWith(":fx")
                ? new[] { "https://api-free.deepl.com", "https://api.deepl.com" }
                : new[] { "https://api.deepl.com", "https://api-free.deepl.com" };
            bool authFail = false;
            string lastErr = "Unknown";
            foreach (var host in hosts)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, host + "/v2/languages?type=target");
                    req.Headers.Add("Authorization", "DeepL-Auth-Key " + key);
                    using var resp = await Http.SendAsync(req);
                    int code = (int)resp.StatusCode;
                    if (resp.IsSuccessStatusCode) return (true, "OK");
                    if (code == 401 || code == 403) { authFail = true; continue; }
                    lastErr = code.ToString();
                }
                catch (Exception ex) { lastErr = ex.Message; }
            }
            return (false, authFail ? "Invalid key" : lastErr);
        }
    }
}
